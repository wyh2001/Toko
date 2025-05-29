using MediatR;
using OneOf;
using Stateless;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Toko.Models.Events;
using Toko.Services;
using static Toko.Services.RoomManager;
using static Toko.Services.CardHelper;
using System.Threading.Tasks;

namespace Toko.Models
{
    public class Room : IAsyncDisposable
    {
        #region Public Properties
        public string Id { get; } = Guid.NewGuid().ToString();
        public string? Name { get; set; }
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; }
        public RaceMap Map { get; set; } = RaceMapFactory.CreateDefaultMap();
        public List<Racer> Racers { get; } = [];
        public int CurrentRound { get; private set; }
        public int CurrentStep => _stepInRound;
        public RoomStatus Status => _gameSM.State;
        #endregion

        #region ▶ FSM
        private enum Phase { CollectingCards, CollectingParams, Discarding }
        public enum RoomStatus { Waiting, Playing, Finished }
        private enum GameTrigger { Start, GameOver }
        private enum PhaseTrigger { CardsReady, ParamsDone, DiscardDone }

        private readonly StateMachine<RoomStatus, GameTrigger> _gameSM;
        private readonly StateMachine<Phase, PhaseTrigger> _phaseSM;
        #endregion

        #region Constants & Fields
        private readonly List<int> _steps;
        private readonly List<string> _order = [];
        private int _idx;
        private int _stepInRound;

        private readonly IMediator _mediator;
        private readonly SemaphoreSlim _gate = new(1, 1);
        //private Timer _promptTimer;

        private readonly Dictionary<string, DateTime> _thinkStart = [];
        private readonly Dictionary<string, TimeSpan> _bank = [];
        private readonly HashSet<string> _banned = [];
        private readonly Dictionary<(string, int, int), string> _cardNow = []; // (pid, round, step) -> cardId
        private HashSet<string> _discardPending = [];

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pumpTask;
        private readonly Dictionary<string, DateTime> _nextPrompt = [];

        private static readonly TimeSpan INIT_BANK = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BANK_INCREMENT = TimeSpan.FromSeconds(10);
        //private static readonly TimeSpan TIMEOUT_PROMPT = TimeSpan.FromSeconds(5);
        private readonly PeriodicTimer _ticker = new(TimeSpan.FromSeconds(1));
        private static readonly TimeSpan KICK_ELIGIBLE = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan DISCARD_AUTO_SKIP = TimeSpan.FromSeconds(15);

        private const int AUTO_DRAW = 5; // Draw 5 cards automatically
        private const int DRAW_ON_SKIP = 1;
        private const string SKIP = "SKIP";

        private bool _disposed;
        private readonly ILogger<Room> _log;
        private readonly ILoggerFactory _loggerFactory;

        private readonly TurnExecutor _turnExecutor;
        #endregion

        public Room(IMediator mediator, IEnumerable<int> stepsPerRound, ILogger<Room> log, ILoggerFactory loggerFactory)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _steps = stepsPerRound?.Any() == true ? stepsPerRound.ToList() : new() { 5 };
            _gameSM = new(RoomStatus.Waiting);
            _phaseSM = new(Phase.CollectingCards);
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            var turnExecutorLogger = _loggerFactory.CreateLogger<TurnExecutor>();
            _turnExecutor = new TurnExecutor(Map, turnExecutorLogger);
            ConfigureFSM();
            _pumpTask = Task.Run(() => PromptPumpAsync(_cts.Token));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        #region Game Control
        public async Task<OneOf<StartRoomSuccess, StartRoomError>> StartGameAsync(string playerId)
        {
            await _gate.WaitAsync();
            try
            {
                if (Status == RoomStatus.Playing) return StartRoomError.AlreadyStarted;
                if (Status == RoomStatus.Finished) return StartRoomError.AlreadyFinished;
                if (Racers.Count == 0) return StartRoomError.NoPlayers;
                var host = Racers.FirstOrDefault(r => r.Id == playerId);
                if (host?.IsHost != true) return StartRoomError.NotHost;
                if (this.Racers.Any(r => !r.IsReady)) return StartRoomError.NotAllReady;

                _order.Clear();
                _order.AddRange(Racers.Select(r => r.Id));
                foreach (var id in _order) 
                { 
                    _bank[id] = INIT_BANK; 
                    _thinkStart[id] = DateTime.UtcNow;
                    _nextPrompt[id] = DateTime.MaxValue;
                }

                CurrentRound = 0; _stepInRound = 0;
                _gameSM.Fire(GameTrigger.Start);
                return new StartRoomSuccess(Id);
            }
            finally { _gate.Release(); }
        }

        public async Task EndGameAsync()
        {
            await _gate.WaitAsync();
            try { _gameSM.Fire(GameTrigger.GameOver); }
            finally { _gate.Release(); }
        }

        public async Task<OneOf<JoinRoomSuccess, JoinRoomError>> JoinRoomAsync(string playerId, string playerName)
        {
            await _gate.WaitAsync();
            try
            {
                if (Racers.Count >= MaxPlayers) return JoinRoomError.RoomFull;
                var racer = new Racer { Id = playerId, PlayerName = playerName };
                InitializeDeck(racer); DrawCardsInternal(racer, 5); 
                Racers.Add(racer);

                // just in case somehow these are not initialized
                if (!_bank.ContainsKey(playerId))
                    _bank[playerId] = INIT_BANK;
                if (!_nextPrompt.ContainsKey(playerId))
                    _nextPrompt[playerId] = DateTime.MaxValue;

                return new JoinRoomSuccess(racer, this.Id);
            }
            finally { _gate.Release(); }
        }

        //public record LeaveRoomSuccess(string PlayerId);
        //public enum LeaveRoomError { PlayerNotFound }
        public async Task<OneOf<LeaveRoomSuccess, LeaveRoomError>> LeaveRoomAsync(string pid)
        {
            await _gate.WaitAsync();
            try
            {
                var racer = Racers.FirstOrDefault(r => r.Id == pid);
                if (racer is null) return LeaveRoomError.PlayerNotFound;
                Racers.Remove(racer);
                _banned.Add(pid); // auto skip
                return new LeaveRoomSuccess(this.Id, pid);
            }
            finally { _gate.Release(); }
        }

        public async Task<OneOf<ReadyUpSuccess, ReadyUpError>> ReadyUpAsync(string pid, bool ready)
        {
            await _gate.WaitAsync();
            try
            {
                var racer = Racers.FirstOrDefault(r => r.Id == pid);
                if (racer is null) return ReadyUpError.PlayerNotFound;
                racer.IsReady = ready;
                return new ReadyUpSuccess(this.Id, pid, ready);
            }
            finally { _gate.Release(); }
        }
        #endregion

        #region Collecting Cards
        public async Task<OneOf<SubmitStepCardSuccess, SubmitStepCardError>> SubmitStepCardAsync(string pid, string cardId)
        {
            await _gate.WaitAsync();
            try { return SubmitStepCard(pid, cardId); }
            finally { _gate.Release(); }
        }

        private OneOf<SubmitStepCardSuccess, SubmitStepCardError>
            SubmitStepCard(string pid, string cardId)
        {
            if (_banned.Contains(pid)) return SubmitStepCardError.PlayerBanned;
            if (_phaseSM.State != Phase.CollectingCards)
                return SubmitStepCardError.WrongPhase;
            if (pid != _order[_idx]) return SubmitStepCardError.NotYourTurn;

            var racer = Racers.FirstOrDefault(r => r.Id == pid)
                       ?? throw new InvalidOperationException();
            var cardObj = racer.Hand.FirstOrDefault(c => c.Id == cardId);
            if (cardObj is null) return SubmitStepCardError.CardNotFound;

            UpdateBank(pid);
            racer.Hand.Remove(cardObj);
            racer.DiscardPile.Add(cardObj);

            //_cardNow[pid] = cardId;
            _cardNow[(pid, CurrentRound, _stepInRound)] = cardId;
            MoveNextPlayer();
            return new SubmitStepCardSuccess(this.Id, pid, cardId);
        }
        #endregion

        #region Collecting Parameters
        public async Task<OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>> SubmitExecutionParamAsync(string pid, ExecParameter p)
        {
            await _gate.WaitAsync();
            try { return SubmitExecutionParam(pid, p); }
            finally { _gate.Release(); }
        }

        private OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>
            SubmitExecutionParam(string pid, ExecParameter p)
        {
            if (_banned.Contains(pid)) return SubmitExecutionParamError.PlayerBanned;
            if (_phaseSM.State != Phase.CollectingParams)
                return SubmitExecutionParamError.WrongPhase;
            if (pid != _order[_idx]) return SubmitExecutionParamError.NotYourTurn;

            var racer = Racers.First(r => r.Id == pid);
            if (!_cardNow.TryGetValue((pid, CurrentRound, _stepInRound), out var cardId))
                return SubmitExecutionParamError.CardNotFound;

            var card = racer.DiscardPile.Concat(racer.Hand).First(c => c.Id == cardId);
            if (!Validate(card.Type, p)) return SubmitExecutionParamError.InvalidExecParameter;

            UpdateBank(pid);

            var ins = new ConcreteInstruction { Type = card.Type, ExecParameter = p };
            _turnExecutor.ApplyInstruction(racer, ins, this);
            _mediator.Publish(new PlayerStepExecuted(Id, CurrentRound, CurrentStep, new()));

            MoveNextPlayer();
            return new SubmitExecutionParamSuccess(this.Id, pid, ins);
        }
        #endregion

        #region Discarding Cards
        public async Task<OneOf<DiscardCardsSuccess, DiscardCardsError>> SubmitDiscardAsync(string pid, List<string> cardIds)
        {
            await _gate.WaitAsync();
            try { return SubmitDiscard(pid, cardIds); }
            finally { _gate.Release(); }
        }

        private OneOf<DiscardCardsSuccess, DiscardCardsError>
            SubmitDiscard(string pid, List<string> cardIds)
        {
            if (_banned.Contains(pid)) return DiscardCardsError.PlayerBanned;
            if (_phaseSM.State != Phase.Discarding)
                return DiscardCardsError.WrongPhase;
            if (!_discardPending.Contains(pid)) return DiscardCardsError.NotYourTurn;

            var racer = Racers.First(r => r.Id == pid);
            if (cardIds.Any(cid =>
                 racer.Hand.All(c => c.Id != cid) ||
                 racer.Hand.First(c => c.Id == cid).Type == CardType.Junk))
                return DiscardCardsError.CardNotFound;

            _turnExecutor.DiscardCards(racer, cardIds);
            _mediator.Publish(new PlayerDiscardExecuted(Id, CurrentRound, CurrentStep, pid, cardIds));

            _discardPending.Remove(pid);
            UpdateBank(pid);
            //RescheduleTimerAsync();
            ResetPrompt(pid);

            if (_discardPending.Count == 0)
                _phaseSM.Fire(PhaseTrigger.DiscardDone);

            return new DiscardCardsSuccess(this.Id, pid, cardIds);
        }
        #endregion

        #region Vote Kick
        public async Task<OneOf<KickVoteSuccess, KickVoteError>> VoteKickAsync(string targetId)
        {
            await _gate.WaitAsync();
            try { return VoteKick(targetId); }
            finally { _gate.Release(); }
        }

        public enum KickVoteError { WrongPhase, TooEarly, TargetNotFound, AlreadyKicked }
        public record KickVoteSuccess(string TargetId);

        private OneOf<KickVoteSuccess, KickVoteError> VoteKick(string targetId)
        {
            if (Status != RoomStatus.Playing) return KickVoteError.WrongPhase;
            if (!_order.Contains(targetId)) return KickVoteError.TargetNotFound;
            if (_banned.Contains(targetId)) return KickVoteError.AlreadyKicked;
            if (!IsKickEligible(targetId)) return KickVoteError.TooEarly;

            _banned.Add(targetId);
            _mediator.Publish(new PlayerKicked(Id, targetId));
            return new KickVoteSuccess(targetId);
        }
        #endregion

        #region FSM Configuration
        void ConfigureFSM()
        {
            _gameSM.Configure(RoomStatus.Waiting)
                   .Permit(GameTrigger.Start, RoomStatus.Playing);

            _gameSM.Configure(RoomStatus.Playing)
                   .OnEntry(() => _phaseSM.Activate())
                   .Permit(GameTrigger.GameOver, RoomStatus.Finished);

            _gameSM.Configure(RoomStatus.Finished);
                   //.OnEntry(() => _cts?.Cancel());

            _phaseSM.Configure(Phase.CollectingCards)
                    .OnEntry(StartCardCollection)
                    .Permit(PhaseTrigger.CardsReady, Phase.CollectingParams);

            _phaseSM.Configure(Phase.CollectingParams)
                    .OnEntry(StartParamCollection)
                    .Permit(PhaseTrigger.ParamsDone, Phase.Discarding);

            _phaseSM.Configure(Phase.Discarding)
                    .OnEntry(StartDiscardPhase)
                    .Permit(PhaseTrigger.DiscardDone, Phase.CollectingCards);

            _phaseSM.OnTransitioned(t =>
            {
                if (t.Trigger == PhaseTrigger.DiscardDone)
                    NextStep();
            });
        }
        void StartCardCollection()
        {
            if (!IsAnyActivePlayer() && Status == RoomStatus.Playing)
            {
                _log.LogInformation("No active players left in room {RoomId}. Ending game.", Id);
                _gameSM.Fire(GameTrigger.GameOver);
                return;
            }
            foreach (var r in Racers) DrawCards(r, AUTO_DRAW);
            _cardNow.Clear();
            _idx = 0;
            _stepInRound = 0;
            PromptCard(_order[0]);
        }

        void StartParamCollection()
        {
            _idx = 0;
            _stepInRound = 0;
            PromptParam(_order[0]);
        }

        void StartDiscardPhase()
        {
            _idx = 0;
            _discardPending = _order.Where(id => !_banned.Contains(id))
                                     .ToHashSet();

            foreach (var pid in _discardPending)
            {
                _thinkStart[pid] = DateTime.UtcNow;
                _mediator.Publish(new PlayerDiscardStarted(Id, CurrentRound, CurrentStep, pid));
                ResetPrompt(pid);
            }
            //RescheduleTimerAsync();
        }
        #endregion

        #region Helper Methods for Prompting Players
        void PromptCard(string pid)
        {
            _thinkStart[pid] = DateTime.UtcNow;
            //RescheduleTimerAsync();
            ResetPrompt(pid);
            _mediator.Publish(new PlayerCardSubmissionStarted(Id, CurrentRound, CurrentStep, pid));
        }

        void PromptParam(string pid)
        {
            //check if draw to skip
            if (_cardNow.TryGetValue((pid, CurrentRound, _stepInRound), out var cardId) && cardId == SKIP)
            {
                _mediator.Publish(new PlayerParameterSubmissionSkipped(Id, CurrentRound, CurrentStep, pid));
                MoveNextPlayer();
                return;
            }
            _thinkStart[pid] = DateTime.UtcNow;
            //RescheduleTimerAsync();
            ResetPrompt(pid);
            _mediator.Publish(new PlayerParameterSubmissionStarted(Id, CurrentRound, CurrentStep, pid));
        }

        #endregion

        #region Other Helper Methods
        static bool Validate(CardType t, ExecParameter p) => t switch
        {
            CardType.Move => p.Effect is 1 or 2,
            CardType.ChangeLane => p.Effect is 1 or -1,
            CardType.Repair => p.DiscardedCardIds.Count > 0,
            _ => false
        };

        void UpdateBank(string pid)
        {
            var elapsed = DateTime.UtcNow - _thinkStart[pid];
            _bank[pid] -= elapsed;
            _bank[pid] += BANK_INCREMENT;
            //if (_bank[pid] < TimeSpan.Zero) _bank[pid] = TimeSpan.Zero;

            _thinkStart[pid] = DateTime.UtcNow;
            ResetPrompt(pid);
        }

        void ResetPrompt(string pid)
        {
            var wait = _bank[pid];
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            _nextPrompt[pid] = DateTime.UtcNow + wait; // next due time
        }

        private async Task PromptPumpAsync(CancellationToken ct)
        {
            try
            {
                while (await _ticker.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    await _gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        if (Status != RoomStatus.Playing) continue;

                        // the phase of discarding
                        if (_phaseSM.State == Phase.Discarding)
                        {
                            foreach (var pid in _discardPending.ToList())
                            {
                                var elapsed = DateTime.UtcNow - _thinkStart[pid];

                                // auto skip after 15 seconds
                                if (elapsed >= DISCARD_AUTO_SKIP)
                                {
                                    _discardPending.Remove(pid);
                                    _thinkStart[pid] = DateTime.UtcNow; // reset think start
                                    await _mediator.Publish(
                                        new PlayerDiscardExecuted(
                                            Id, CurrentRound, CurrentStep, pid, new List<string>()), ct);

                                    // if no more players pending discard, then move to next phase
                                    if (_discardPending.Count == 0)
                                        _phaseSM.Fire(PhaseTrigger.DiscardDone);
                                }
                                else if (IsTimeOut(pid))
                                {
                                    await _mediator.Publish(
                                        new PlayerTimeoutElapsed(Id, pid), ct);
                                }
                            }

                            continue;
                        }

                        // the phase of collecting cards or parameters
                        var pidTurn = _order[_idx]; // current turn player's id
                        if (IsTimeOut(pidTurn))
                        {
                            await _mediator.Publish(
                                new PlayerTimeoutElapsed(Id, pidTurn), ct);

                            _nextPrompt[pidTurn] = DateTime.MaxValue;
                        }
                    }
                    finally { _gate.Release(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.LogError(ex, "pump crashed"); }
        }


        bool IsKickEligible(string pid) => DateTime.UtcNow - _thinkStart[pid] >= KICK_ELIGIBLE;
        bool IsTimeOut(string pid) => DateTime.UtcNow >= _nextPrompt[pid];
        bool IsAnyActivePlayer() => _order.Any(pid => !_banned.Contains(pid));

        void MoveNextPlayer()
        {
            do { _idx++; } while (_idx < _order.Count && _banned.Contains(_order[_idx]));

            if (_idx == _order.Count)
            {
                _idx = 0;
                if (++_stepInRound >= _steps[CurrentRound])
                {
                    var trigger = _phaseSM.State == Phase.CollectingCards ? PhaseTrigger.CardsReady : PhaseTrigger.ParamsDone;
                    _phaseSM.Fire(trigger);
                    return;
                }
            }


            if (_phaseSM.State == Phase.CollectingCards) PromptCard(_order[_idx]);
            else if (_phaseSM.State == Phase.CollectingParams) PromptParam(_order[_idx]);
            
        }

        void NextStep()
        {
            _stepInRound = 0;
            if (++CurrentRound >= _steps.Count)
                _gameSM.Fire(GameTrigger.GameOver);

        }

        public async Task<OneOf<DrawSkipSuccess, DrawSkipError>> DrawSkipAsync(string pid)
        {
            await _gate.WaitAsync();
            try { return DrawSkip(pid); }
            finally { _gate.Release(); }
        }

        private OneOf<DrawSkipSuccess, DrawSkipError> DrawSkip(string pid)
        {
            if (_banned.Contains(pid)) return DrawSkipError.PlayerBanned;
            if (_phaseSM.State != Phase.CollectingCards)
                return DrawSkipError.WrongPhase;
            if (pid != _order[_idx]) return DrawSkipError.NotYourTurn;

            var racer = Racers.FirstOrDefault(r => r.Id == pid)
                       ?? throw new InvalidOperationException();
            UpdateBank(pid);
            var result = DrawCards(racer, DRAW_ON_SKIP);
            _mediator.Publish(new PlayerDrawToSkip(Id, CurrentRound, CurrentStep, pid));
            _cardNow[(pid, CurrentRound, _stepInRound)] = SKIP;
            MoveNextPlayer();
            return new DrawSkipSuccess(this.Id, pid, result);
        }

        public List<Card> DrawCards(Racer racer, int requestedCount)
        {
            int space = racer.HandCapacity - racer.Hand.Count;
            int toDraw = Math.Min(requestedCount, Math.Max(0, space));
            var drawn = DrawCardsInternal(racer, toDraw);
            return drawn;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _cts.Cancel();
            _ticker.Dispose();
            try { await _pumpTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }

            _gate.Dispose();
            _cts.Dispose();
        }
        #endregion
    }
}
