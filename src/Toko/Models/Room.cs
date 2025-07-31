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
using Toko.Shared.Models;
using Toko.Shared.Services;
using System.Collections.Concurrent;

namespace Toko.Models
{
    public class Room : IAsyncDisposable
    {
        #region Public Properties
        public string Id { get; } = Guid.NewGuid().ToString();
        public string? Name { get; set; }
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; }
        public bool IsAbandoned { get; private set; }
        public RaceMap Map { get; set; } = RaceMapFactory.CreateDefaultMap();
        public List<Racer> Racers { get; } = [];
        public int CurrentRound { get; private set; }
        public int CurrentStep => _stepInRound;
        public RoomStatus Status => _gameSM.State;
        #endregion

        #region ▶ FSM
        //public enum Phase { CollectingCards, CollectingParams, Discarding }
        public enum RoomStatus { Waiting, Playing, Finished }
        private enum GameTrigger { Start, GameOver }
        private enum PhaseTrigger { CardsReady, ParamsDone, DiscardDone }

        private readonly StateMachine<RoomStatus, GameTrigger> _gameSM;
        private readonly StateMachine<Phase, PhaseTrigger> _phaseSM;
        #endregion

        #region Constants & Fields
        private List<int> _steps;
        private readonly List<string> _order = [];
        private int _idx;
        private int _stepInRound;

        private readonly IEventChannel _events;
        private readonly SemaphoreSlim _gate = new(1, 1);
        //private Timer _promptTimer;

        private readonly Dictionary<string, DateTime> _thinkStart = [];
        private readonly Dictionary<string, TimeSpan> _bank = [];
        private readonly HashSet<string> _banned = [];
        private readonly Dictionary<(string, int, int), (string cardId, CardType cardType)> _cardNow = []; // (pid, round, step) -> (cardId, cardType)
        private readonly HashSet<string> _discardPending = [];
        private List<PlayerResult>? _gameResults = null; // Cached game results
        
        // Track gear shift counts for turn order adjustment
        private readonly Dictionary<string, int> _currentTurnGearShifts = [];

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
        public const int INITIALDRAW = 5; // Initial draw for new players

        private bool _disposed;
        private readonly ILogger<Room> _log;
        private readonly ILoggerFactory _loggerFactory;

        private readonly TurnExecutor _turnExecutor;
        private readonly ConcurrentStack<TurnLog> _logs = new();
        private volatile int _logCount = 0;
        
        // Maximum number of logs to prevent memory issues
        private const int MAX_LOGS = 10000;
        #endregion

        public Room(IEventChannel events, IEnumerable<int> stepsPerRound, ILogger<Room> log, ILoggerFactory loggerFactory)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _steps = (stepsPerRound != null && stepsPerRound.Any()) ? [.. stepsPerRound] : [5];
            _gameSM = new(RoomStatus.Waiting);
            _phaseSM = new(Phase.CollectingCards);
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            var turnExecutorLogger = _loggerFactory.CreateLogger<TurnExecutor>();
            _turnExecutor = new TurnExecutor(Map, turnExecutorLogger);
            ConfigureFSM();
            _pumpTask = Task.Run(() => PromptPumpAsync(_cts.Token));
        }

        #region Game Control
        public async Task<OneOf<StartRoomSuccess, StartRoomError>> StartGameAsync(string playerId)
        {
            return await WithGateAsync<OneOf<StartRoomSuccess, StartRoomError>>(events =>
            {
                if (Status == RoomStatus.Playing) return StartRoomError.AlreadyStarted;
                if (Status == RoomStatus.Finished) return StartRoomError.AlreadyFinished;
                if (Racers.Count == 0) return StartRoomError.NoPlayers;
                var host = Racers.FirstOrDefault(r => r.Id == playerId);
                if (host is null) return StartRoomError.NotInTheRoom;
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
                events.Add(new RoomStarted(Id, _order));
                return new StartRoomSuccess(Id);
            });
        }

        //public enum GameEndReason
        //{
        //    FinisherCrossedLine,
        //    NoActivePlayersLeft,
        //    TurnLimitReached,
        //}
        //public record PlayerResult(string PlayerId, int Rank, int Score);

        // Collects and returns the game results, ranking players by progress and assigning scores (total cells passed)
        private List<PlayerResult> CollectGameResults()
        {

            var progressList = Racers.Select(r => new
            {
                Racer = r,
                Score = r.Score // Use the real-time score maintained by TurnExecutor
            }).ToList();

            // Sort by score descending
            var ranked = progressList
                .OrderByDescending(x => x.Score)
                .ToList();

            var results = new List<PlayerResult>();
            int rank = 0;
            int prevScore = -1;
            int sameRankCount = 0;
            foreach (var entry in ranked)
            {
                // If score is same as previous, use same rank
                if (entry.Score == prevScore)
                {
                    sameRankCount++;
                }
                else
                {
                    rank += sameRankCount + 1;
                    sameRankCount = 0;
                }
                prevScore = entry.Score;
                results.Add(new PlayerResult(entry.Racer.Id, rank, entry.Score));
            }
            return results;
        }

        public async Task<List<PlayerResult>> EndGameAsync(GameEndReason reason)
        {
            return await WithGateAsync(events =>
            {
                var results = EndGame(reason);
                events.Add(new GameEnded(Id, reason, results));
                return results;
            });
        }

        private List<PlayerResult> EndGame(GameEndReason reason)
        {
            _gameSM.Fire(GameTrigger.GameOver);
            _gameResults ??= CollectGameResults();
            return _gameResults;
        }

        public async Task<OneOf<JoinRoomSuccess, JoinRoomError>> JoinRoomAsync(string playerId, string playerName)
        {
            return await WithGateAsync<OneOf<JoinRoomSuccess, JoinRoomError>>(events =>
            {
                // prevent from joining if room is abandoned
                if (IsAbandoned) return JoinRoomError.RoomNotFound;
                // prevent from joining if already joined
                if (Racers.Any(r => r.Id == playerId)) return JoinRoomError.AlreadyJoined;
                if (Racers.Count >= MaxPlayers) return JoinRoomError.RoomFull;
                var racer = new Racer { Id = playerId, PlayerName = playerName };
                
                AssignStartingPosition(racer);
                
                InitializeDeck(racer); DrawCardsInternal(racer, INITIALDRAW);
                Racers.Add(racer);
                events.Add(new PlayerJoined(Id, racer.Id, racer.PlayerName));

                // just in case somehow these are not initialized
                if (!_bank.ContainsKey(playerId))
                    _bank[playerId] = INIT_BANK;
                if (!_nextPrompt.ContainsKey(playerId))
                    _nextPrompt[playerId] = DateTime.MaxValue;

                return new JoinRoomSuccess(racer, this.Id);
            });
        }

        public async Task<OneOf<LeaveRoomSuccess, LeaveRoomError>> LeaveRoomAsync(string pid)
        {
            return await WithGateAsync<OneOf<LeaveRoomSuccess, LeaveRoomError>>(events =>
            {
                var racer = Racers.FirstOrDefault(r => r.Id == pid);
                if (racer is null) return LeaveRoomError.PlayerNotFound;
                switch (Status)
                {
                    case RoomStatus.Waiting:
                        if (racer.IsHost)
                        {
                            // change host to the next player if exists
                            var nextHost = Racers.FirstOrDefault(r => r.Id != pid);
                            if (nextHost is not null)
                            {
                                nextHost.IsHost = true;
                                events.Add(new HostChanged(Id, nextHost.Id, nextHost.PlayerName));
                            }
                            // the empty room will be deleted later by the RoomManager
                        }
                        string playerName = racer.PlayerName;
                        Racers.Remove(racer);

                        // If this was the last player leaving a waiting room, mark it as abandoned
                        if (Racers.Count == 0)
                        {
                            IsAbandoned = true;
                            events.Add(new RoomAbandoned(Id));
                            _log.LogInformation("Room {RoomId} marked as abandoned after last player left", Id);
                        }

                        events.Add(new PlayerLeft(Id, pid, playerName));
                        return new LeaveRoomSuccess(this.Id, pid);

                    case RoomStatus.Playing:
                        _banned.Add(pid); // auto skip
                        events.Add(new PlayerLeft(Id, pid, racer.PlayerName));
                        return new LeaveRoomSuccess(this.Id, pid);

                    case RoomStatus.Finished:
                        return LeaveRoomError.AlreadyFinished;

                    default:
                        throw new InvalidOperationException("Unexpected room status when leaving.");
                }
            });
        }

        private string GetPlayerName(string playerId)
        {
            var racer = Racers.FirstOrDefault(r => r.Id == playerId);
            return racer?.PlayerName ?? "Unknown Player";
        }

        public async Task<OneOf<ReadyUpSuccess, ReadyUpError>> ReadyUpAsync(string pid, bool ready)
        {
            return await WithGateAsync<OneOf<ReadyUpSuccess, ReadyUpError>>(events =>
            {
                var racer = Racers.FirstOrDefault(r => r.Id == pid);
                if (racer is null) return ReadyUpError.PlayerNotFound;
                racer.IsReady = ready;
                events.Add(new PlayerReadyToggled(Id, pid, racer.PlayerName, ready));
                return new ReadyUpSuccess(this.Id, pid, ready);
            });
        }
        #endregion

        #region Collecting Cards
        public async Task<OneOf<SubmitStepCardSuccess, SubmitStepCardError>> SubmitStepCardAsync(string pid, string cardId)
        {
            return await WithGateAsync<OneOf<SubmitStepCardSuccess, SubmitStepCardError>>(async events =>
            {
                if (_banned.Contains(pid)) return SubmitStepCardError.PlayerBanned;
                if (_phaseSM.State != Phase.CollectingCards)
                    return SubmitStepCardError.WrongPhase;
                if (pid != _order[_idx]) return SubmitStepCardError.NotYourTurn;

                var racer = Racers.FirstOrDefault(r => r.Id == pid)
                       ?? throw new InvalidOperationException();
                var cardObj = racer.Hand.FirstOrDefault(c => c.Id == cardId);
                if (cardObj is null) return SubmitStepCardError.CardNotFound;
                if (cardObj.Type == CardType.Junk)
                    return SubmitStepCardError.InvalidCardType;

                UpdateBank(pid, events);
                racer.Hand.Remove(cardObj);
                racer.DiscardPile.Add(cardObj);

                // Track gear shift count if this is a gear shift card
                if (cardObj.Type == CardType.ShiftGear)
                {
                    _currentTurnGearShifts[pid] = _currentTurnGearShifts.GetValueOrDefault(pid, 0) + 1;
                }

                _cardNow[(pid, CurrentRound, _stepInRound)] = (cardId, cardObj.Type);
                events.Add(new PlayerCardSubmitted(Id, CurrentRound, CurrentStep, pid, racer.PlayerName, cardId, cardObj.Type));
                await MoveNextPlayerAsync(events);
                return new SubmitStepCardSuccess(this.Id, pid, cardId);
            });
        }
        #endregion

        #region Collecting Parameters
        public async Task<OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>> SubmitExecutionParamAsync(string pid, ExecParameter p)
        {
            return await WithGateAsync<OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>>(async events =>
            {
                if (_banned.Contains(pid)) return SubmitExecutionParamError.PlayerBanned;
                if (_phaseSM.State != Phase.CollectingParams)
                    return SubmitExecutionParamError.WrongPhase;
                if (pid != _order[_idx]) return SubmitExecutionParamError.NotYourTurn;

                var racer = Racers.FirstOrDefault(r => r.Id == pid);
                if (racer is null) return SubmitExecutionParamError.PlayerNotFound;
                if (!_cardNow.TryGetValue((pid, CurrentRound, _stepInRound), out var cardInfo))
                    return SubmitExecutionParamError.CardNotFound;

                var (cardId, cardType) = cardInfo;

                // Special case: Repair card with no discarded cards is treated as auto-skip
                if (cardType == CardType.Repair && p.DiscardedCardIds.Count == 0)
                {
                    UpdateBank(pid, events);
                    events.Add(new PlayerParameterSubmissionSkipped(Id, CurrentRound, CurrentStep, pid, racer.PlayerName));
                    await MoveNextPlayerAsync(events);
                    // Return success with a no-op instruction
                    var skipInstruction = new ConcreteInstruction { Type = cardType, ExecParameter = p };
                    return new SubmitExecutionParamSuccess(this.Id, pid, skipInstruction);
                }

                if (!Validate(cardType, p)) return SubmitExecutionParamError.InvalidExecParameter;

                UpdateBank(pid, events);

                var ins = new ConcreteInstruction { Type = cardType, ExecParameter = p };
                
                var executionResult = _turnExecutor.ApplyInstruction(racer, ins, this, events);
                if (executionResult == TurnExecutor.TurnExecutionResult.PlayerFinished)
                {
                    events.Add(new PlayerFinished(Id, CurrentRound, CurrentStep, pid, racer.PlayerName));
                    _gameResults ??= CollectGameResults(); 
                    events.Add(new GameEnded(Id, GameEndReason.FinisherCrossedLine, _gameResults));
                    _gameSM.Fire(GameTrigger.GameOver);
                }
                events.Add(new PlayerStepExecuted(Id, CurrentRound, CurrentStep));
                
                // Execute automatic movement based on gear after card execution
                var autoMoveResult = _turnExecutor.ExecuteAutoMove(racer, this, events);
                if (autoMoveResult == TurnExecutor.TurnExecutionResult.PlayerFinished)
                {
                    events.Add(new PlayerFinished(Id, CurrentRound, CurrentStep, pid, racer.PlayerName));
                    _gameResults ??= CollectGameResults(); 
                    events.Add(new GameEnded(Id, GameEndReason.FinisherCrossedLine, _gameResults));
                    _gameSM.Fire(GameTrigger.GameOver);
                }
                
                await MoveNextPlayerAsync(events);
                return new SubmitExecutionParamSuccess(this.Id, pid, ins);
            });
        }
        #endregion

        #region Discarding Cards
        public async Task<OneOf<DiscardCardsSuccess, DiscardCardsError>> SubmitDiscardAsync(string pid, List<string> cardIds)
        {
            return await WithGateAsync<OneOf<DiscardCardsSuccess, DiscardCardsError>>(async events =>
            {
                if (_banned.Contains(pid)) return DiscardCardsError.PlayerBanned;
                if (_phaseSM.State != Phase.Discarding)
                    return DiscardCardsError.WrongPhase;
                if (!_discardPending.Contains(pid)) return DiscardCardsError.NotYourTurn;

                var racer = Racers.FirstOrDefault(r => r.Id == pid);
                if (racer is null) return DiscardCardsError.PlayerNotFound;

                // If cardIds is empty, allow it (player chooses not to discard)
                if (cardIds.Count > 0)
                {
                    // Check if all cards exist in hand (but allow any card type to be discarded)
                    if (cardIds.Any(cid => racer.Hand.All(c => c.Id != cid)))
                        return DiscardCardsError.CardNotFound;

                    // Ensure no card is junk
                    if (cardIds.Any(cid =>
                        racer.Hand.FirstOrDefault(c => c.Id == cid)?.Type == CardType.Junk))
                        return DiscardCardsError.InvalidCardType;
                }

                _turnExecutor.DiscardCards(racer, cardIds);
                events.Add(new PlayerDiscardExecuted(Id, CurrentRound, CurrentStep, pid, racer.PlayerName, cardIds));

                _discardPending.Remove(pid);
                UpdateBank(pid, events);
                ResetPrompt(pid);

                if (_discardPending.Count == 0)
                    await _phaseSM.FireAsync(PhaseTrigger.DiscardDone);

                return new DiscardCardsSuccess(this.Id, pid, cardIds);
            });
        }
        #endregion

        #region Vote Kick
        public async Task<OneOf<KickPlayerSuccess, KickPlayerError>> KickPlayerAsync(string playerId, string kickedPlayerId)
        {
            return await WithGateAsync<OneOf<KickPlayerSuccess, KickPlayerError>>(events =>
            {
                // find the kicker
                var kicker = Racers.FirstOrDefault(r => r.Id == playerId);
                if (kicker is null) return KickPlayerError.PlayerNotFound;
                if (Status == RoomStatus.Waiting)
                {
                    if (!kicker.IsHost) return KickPlayerError.NotHost;
                    // if the room is waiting, just remove the player
                    var targetRacer = Racers.FirstOrDefault(r => r.Id == kickedPlayerId);
                    if (targetRacer is null) return KickPlayerError.TargetNotFound;
                    Racers.Remove(targetRacer);
                    events.Add(new PlayerKicked(Id, kickedPlayerId, targetRacer.PlayerName));
                    return new KickPlayerSuccess(Id, playerId, kickedPlayerId);
                }
                if (Status != RoomStatus.Playing) return KickPlayerError.WrongPhase;
                if (!_order.Contains(kickedPlayerId)) return KickPlayerError.TargetNotFound;
                if (_banned.Contains(kickedPlayerId)) return KickPlayerError.AlreadyKicked;
                if (!IsKickEligible(kickedPlayerId)) return KickPlayerError.TooEarly;

                _banned.Add(kickedPlayerId);
                events.Add(new PlayerKicked(Id, kickedPlayerId, GetPlayerName(kickedPlayerId)));
                return new KickPlayerSuccess(Id, playerId, kickedPlayerId);
            });
        }
        #endregion

        public async Task<OneOf<UpdateRoomSettingsSuccess, UpdateRoomSettingsError>> UpdateSettingsAsync(string playerId, RoomSettings settings)
        {
            return await WithGateAsync<OneOf<UpdateRoomSettingsSuccess, UpdateRoomSettingsError>>(events =>
            {
                if (Status != RoomStatus.Waiting) return UpdateRoomSettingsError.WrongPhase;
                var host = Racers.FirstOrDefault(r => r.Id == playerId);
                if (host is null) return UpdateRoomSettingsError.PlayerNotFound;
                if (!host.IsHost) return UpdateRoomSettingsError.NotHost;
                Name = settings.Name ?? Name;
                MaxPlayers = settings.MaxPlayers ?? MaxPlayers;
                IsPrivate = settings.IsPrivate ?? IsPrivate;
                _steps = settings.StepsPerRound != null ? [.. settings.StepsPerRound] : _steps;
                events.Add(new RoomSettingsUpdated(Id, settings));
                return new UpdateRoomSettingsSuccess(Id, playerId, settings);
            });
        }

        #region FSM Configuration
        void ConfigureFSM()
        {
            _gameSM.Configure(RoomStatus.Waiting)
                   .Permit(GameTrigger.Start, RoomStatus.Playing);

            _gameSM.Configure(RoomStatus.Playing)
                   .OnEntry(() => _phaseSM.Activate())
                   .Permit(GameTrigger.GameOver, RoomStatus.Finished);

            _gameSM.Configure(RoomStatus.Finished);

            _phaseSM.Configure(Phase.CollectingCards)
                    .OnEntryAsync(StartCardCollectionAsync)
                    .Permit(PhaseTrigger.CardsReady, Phase.CollectingParams);

            _phaseSM.Configure(Phase.CollectingParams)
                    .OnEntry(StartParamCollection)
                    .Permit(PhaseTrigger.ParamsDone, Phase.Discarding);

            _phaseSM.Configure(Phase.Discarding)
                    .OnEntryAsync(StartDiscardPhaseAsync)
                    .Permit(PhaseTrigger.DiscardDone, Phase.CollectingCards);

            _phaseSM.OnTransitionedAsync(async t =>
            {
                await _events.PublishAsync(new PhaseChanged(Id, t.Destination, CurrentRound, CurrentStep));
                if (t.Trigger == PhaseTrigger.DiscardDone)
                    await NextStepAsync();
            });
        }

        async Task StartCardCollectionAsync()
        {
            if (!IsAnyActivePlayer() && Status == RoomStatus.Playing)
            {
                _log.LogInformation("No active players left in room {RoomId}. Ending game.", Id);
                var results = EndGame(GameEndReason.NoActivePlayersLeft);
                await _events.PublishAsync(new GameEnded(Id, GameEndReason.NoActivePlayersLeft, results));
                return;
            }
            var eventsToPublish = new List<IEvent>();
            foreach (var r in Racers)
            {
                DrawCards(r, AUTO_DRAW);
                eventsToPublish.Add(new PlayerCardsDrawn(Id, CurrentRound, CurrentStep, r.Id, r.PlayerName, r.Hand.Count));
            }
            await PublishEventsAsync(eventsToPublish);
            _cardNow.Clear();
            _idx = 0;
            _stepInRound = 0;
            await _events.PublishAsync(PromptCard(_order[0]));
        }

        void StartParamCollection()
        {
            _idx = 0;
            _stepInRound = 0;
        }

        async Task StartDiscardPhaseAsync()
        {
            _idx = 0;
            _stepInRound = 0;
            _discardPending.Clear();
            _discardPending.UnionWith(_order.Where(id => !_banned.Contains(id)));

            // Adjust turn order based on gear shift frequency
            AdjustTurnOrderByGearShifts();

            var eventsToPublish = new List<IEvent>();
            foreach (var pid in _discardPending)
            {
                _thinkStart[pid] = DateTime.UtcNow;
                eventsToPublish.Add(new PlayerDiscardStarted(Id, CurrentRound, CurrentStep, pid, GetPlayerName(pid)));
                ResetPrompt(pid);
            }
            await PublishEventsAsync(eventsToPublish);
        }
        
        private void AdjustTurnOrderByGearShifts()
        {
            // Create list of players with their gear shift counts
            var playersWithShifts = _order
                .Where(pid => !_banned.Contains(pid))
                .Select(pid => new { PlayerId = pid, ShiftCount = _currentTurnGearShifts.GetValueOrDefault(pid, 0) })
                .ToList();

            // Sort players: first by gear shift count (ascending), then maintain original order for same counts
            var sortedPlayers = playersWithShifts
                .OrderBy(p => p.ShiftCount)
                .ThenBy(p => _order.IndexOf(p.PlayerId))
                .Select(p => p.PlayerId)
                .ToList();

            // Update the order list
            _order.Clear();
            _order.AddRange(sortedPlayers);

            // Clear gear shift counts for next turn
            _currentTurnGearShifts.Clear();
        }
        #endregion

        #region Helper Methods for Prompting Players
        PlayerCardSubmissionStarted PromptCard(string pid)
        {
            _thinkStart[pid] = DateTime.UtcNow;
            ResetPrompt(pid);
            return new PlayerCardSubmissionStarted(Id, CurrentRound, CurrentStep, pid, GetPlayerName(pid));
        }

        async Task PromptParamAsync(string pid, List<IEvent> events)
        {
            //check if draw to skip
            if (_cardNow.TryGetValue((pid, CurrentRound, _stepInRound), out var cardInfo) && cardInfo.cardId == SKIP)
            {
                // Even if draw to skip, the player still moves forward automatically
                var racer = Racers.FirstOrDefault(r => r.Id == pid);
                if (racer != null)
                {
                    var autoMoveResult = _turnExecutor.ExecuteAutoMove(racer, this, events);
                    if (autoMoveResult == TurnExecutor.TurnExecutionResult.PlayerFinished)
                    {
                        events.Add(new PlayerFinished(Id, CurrentRound, CurrentStep, pid, GetPlayerName(pid)));
                        _gameResults ??= CollectGameResults();
                        events.Add(new GameEnded(Id, GameEndReason.FinisherCrossedLine, _gameResults));
                        _gameSM.Fire(GameTrigger.GameOver);
                    }
                }

                events.Add(new PlayerParameterSubmissionSkipped(Id, CurrentRound, CurrentStep, pid, GetPlayerName(pid)));
                await MoveNextPlayerAsync(events);
                return;
            }

            // Get the card type from stored card info
            var (cardId, cardType) = cardInfo;

            _thinkStart[pid] = DateTime.UtcNow;
            ResetPrompt(pid);
            events.Add(new PlayerParameterSubmissionStarted(Id, CurrentRound, CurrentStep, pid, GetPlayerName(pid), cardType));
        }
        #endregion

        #region Other Helper Methods
        static bool Validate(CardType t, ExecParameter p) => t switch
        {
            CardType.ChangeLane => p.Effect is 1 or -1,
            CardType.Repair => p.DiscardedCardIds.Count > 0,
            CardType.ShiftGear => p.Effect is 1 or -1, // 1 for shift up, -1 for shift down
            _ => false
        };

        void UpdateBank(string pid, List<IEvent> events)
        {
            var elapsed = DateTime.UtcNow - _thinkStart[pid];
            _bank[pid] -= elapsed;
            _bank[pid] += BANK_INCREMENT;
            events.Add(new PlayerBankUpdated(Id, pid, GetPlayerName(pid), _bank[pid]));

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
                    var events = new List<IEvent>();
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
                                    events.Add(new PlayerDiscardExecuted(Id, CurrentRound, CurrentStep, pid, GetPlayerName(pid), new List<string>()));
                                    events.Add(new PlayerBankUpdated(Id, pid, GetPlayerName(pid), _bank[pid]));

                                    // if no more players pending discard, then move to next phase
                                    if (_discardPending.Count == 0)
                                        _phaseSM.Fire(PhaseTrigger.DiscardDone);
                                }
                                else if (IsTimeOut(pid))
                                {
                                    events.Add(new PlayerTimeoutElapsed(Id, pid, GetPlayerName(pid)));
                                }
                            }

                            continue;
                        }

                        // the phase of collecting cards or parameters
                        var pidTurn = _order[_idx]; // current turn player's id
                        if (IsTimeOut(pidTurn))
                        {
                            events.Add(new PlayerTimeoutElapsed(Id, pidTurn, GetPlayerName(pidTurn)));
                            events.Add(new PlayerBankUpdated(Id, pidTurn, GetPlayerName(pidTurn), _bank[pidTurn]));

                            _nextPrompt[pidTurn] = DateTime.MaxValue;
                        }
                    }
                    finally { _gate.Release(); }

                    // Publish events outside the lock
                    if (events.Any())
                        await PublishEventsAsync(events, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.LogError(ex, "pump crashed"); }
        }

        bool IsKickEligible(string pid)
        {
            if (_thinkStart.TryGetValue(pid, out var thinkStart))
            {
                return DateTime.UtcNow - thinkStart >= KICK_ELIGIBLE;
            }
            return false;
        }

        bool IsTimeOut(string pid)
        {
            if (_nextPrompt.TryGetValue(pid, out var nextPrompt))
            {
                return DateTime.UtcNow >= nextPrompt;
            }
            return false;
        }

        bool IsAnyActivePlayer() => _order.Any(pid => !_banned.Contains(pid));

        async Task MoveNextPlayerAsync(List<IEvent> events)
        {
            do { _idx++; } while (_idx < _order.Count && _banned.Contains(_order[_idx]));

            if (_idx == _order.Count)
            {
                _idx = 0;
                if (++_stepInRound >= _steps[CurrentRound])
                {
                    var trigger = _phaseSM.State == Phase.CollectingCards ? PhaseTrigger.CardsReady : PhaseTrigger.ParamsDone;
                    var nextPhase = _phaseSM.State == Phase.CollectingCards ? "CollectingParams" : "Discarding";
                    events.Add(new StepAdvanced(Id, CurrentRound, _stepInRound, nextPhase));
                    var oldState = _phaseSM.State;
                    await _phaseSM.FireAsync(trigger);
                    var newState = _phaseSM.State;
                    if (oldState == Phase.CollectingCards && newState == Phase.CollectingParams)
                    {
                        await PromptParamAsync(_order[0], events);
                    }
                    return;
                }
                events.Add(new StepAdvanced(Id, CurrentRound, _stepInRound));
            }

            if (_phaseSM.State == Phase.CollectingCards) events.Add(PromptCard(_order[_idx]));
            else if (_phaseSM.State == Phase.CollectingParams) await PromptParamAsync(_order[_idx], events);
        }

        async Task NextStepAsync(CancellationToken ct = default)
        {
            _stepInRound = 0;
            if (++CurrentRound >= _steps.Count)
            {
                var results = EndGame(GameEndReason.TurnLimitReached);
                try
                {
                    await _events.PublishAsync(new GameEnded(Id, GameEndReason.TurnLimitReached, results), ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error publishing RoomEnded event for Room {RoomId}", Id);
                }
            }
            else
            {
                await _events.PublishAsync(new RoundAdvanced(Id, CurrentRound), ct);
            }
        }

        public async Task<OneOf<DrawSkipSuccess, DrawSkipError>> DrawSkipAsync(string pid)
        {
            return await WithGateAsync<OneOf<DrawSkipSuccess, DrawSkipError>>(async events =>
            {
                if (_banned.Contains(pid)) return DrawSkipError.PlayerBanned;
                if (_phaseSM.State != Phase.CollectingCards)
                    return DrawSkipError.WrongPhase;
                if (pid != _order[_idx]) return DrawSkipError.NotYourTurn;

                var racer = Racers.FirstOrDefault(r => r.Id == pid)
                       ?? throw new InvalidOperationException();
                UpdateBank(pid, events);
                var result = DrawCards(racer, DRAW_ON_SKIP);
                events.Add(new PlayerDrawToSkip(Id, CurrentRound, CurrentStep, pid, GetPlayerName(pid)));
                _cardNow[(pid, CurrentRound, _stepInRound)] = (SKIP, CardType.ChangeLane); // Use dummy CardType for skip
                await MoveNextPlayerAsync(events);
                return new DrawSkipSuccess(this.Id, pid, result);
            });
        }

        public List<Card> DrawCards(Racer racer, int requestedCount)
        {
            int space = racer.HandCapacity - racer.Hand.Count;
            int toDraw = Math.Min(requestedCount, Math.Max(0, space));
            var drawn = DrawCardsInternal(racer, toDraw);
            return drawn;
        }

        public async Task<OneOf<GetHandSuccess, GetHandError>> GetHandAsync(string playerId)
        {
            return await WithGateAsync<OneOf<GetHandSuccess, GetHandError>>(events =>
            {
                var racer = Racers.FirstOrDefault(r => r.Id == playerId);
                if (racer is null)
                    return GetHandError.PlayerNotFound;
                // Return a copy to avoid external mutation
                var handCopy = racer.Hand.Select(card => new Card
                {
                    Id = card.Id,
                    Type = card.Type
                }).ToList();
                return new GetHandSuccess(this.Id, playerId, handCopy);
            });
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

        #region Snapshot for API
        // Returns a snapshot of the current room status for API

        
        public void AddLog(TurnLog log)
        {
            AddLogInternal(log);
        }
        
        private void AddLogInternal(TurnLog log)
        {
            var currentCount = Interlocked.Increment(ref _logCount);
            if (currentCount <= MAX_LOGS)
            {
                _logs.Push(log);
            }
            else
            {
                Interlocked.Decrement(ref _logCount);
                _log.LogWarning("Log limit exceeded for room {RoomId}. Current log count: {Count}", Id, currentCount);
            }
        }
        
        public IReadOnlyList<TurnLog> GetRecentLogs(int last = 10)
        {
            var recentLogs = _logs.Take(last).ToArray();
            Array.Reverse(recentLogs);
            return recentLogs;
        }
        
        public async Task<RoomStatusSnapshot> GetStatusSnapshotAsync()
        {
            return await WithGateAsync(events =>
            {
                var phase = _phaseSM.State.ToString();
                var map = new MapSnapshot(
                    Map.TotalCells,
                    Map.Segments.Select(seg => new MapSegmentSnapshot(
                        seg.DefaultType.ToString(),
                        seg.LaneCount,
                        seg.CellCount,
                        seg.Direction.ToString(),
                        seg.IsIntermediate
                    )).ToList()
                );

                // Get current turn card type if in CollectingParams phase
                string? currentTurnCardType = null;
                var currentTurnPlayerId = _order.ElementAtOrDefault(_idx);
                if (_phaseSM.State == Phase.CollectingParams && currentTurnPlayerId != null)
                {
                    if (_cardNow.TryGetValue((currentTurnPlayerId, CurrentRound, _stepInRound), out var cardInfo) && cardInfo.cardId != SKIP)
                    {
                        currentTurnCardType = cardInfo.cardType.ToString();
                    }
                }

                // Fix array bounds issue when game ends due to turn limit
                var totalSteps = CurrentRound < _steps.Count ? _steps[CurrentRound] : _steps.Last();

                var racerStatuses = Racers.Select(r => new RacerStatus(
                    r.Id,
                    r.PlayerName,
                    r.SegmentIndex,
                    r.LaneIndex,
                    r.CellIndex,
                    _bank.TryGetValue(r.Id, out var bank) ? Math.Round(bank.TotalSeconds, 2) : 0.0,
                    r.IsHost,
                    r.IsReady,
                    r.Hand.Count,
                    _banned.Contains(r.Id),
                    r.Gear
                )).ToList();

                _thinkStart.TryGetValue(currentTurnPlayerId ?? "", out var turnStartTime);
                
                var latestTurnLogs = GetRecentLogs(10).ToList();

                // Build turn order with gear shift counts
                var turnOrder = _order.Select(playerId => new TurnOrderStatus(
                    playerId,
                    GetPlayerName(playerId),
                    _currentTurnGearShifts.GetValueOrDefault(playerId, 0)
                )).ToList();

                return new RoomStatusSnapshot(
                    Id,
                    Name ?? string.Empty, // Ensure Name is not null
                    MaxPlayers,
                    IsPrivate,
                    Status.ToString(),
                    phase,
                    CurrentRound,
                    CurrentStep,
                    _steps.Count, // TotalRounds
                    totalSteps, // TotalSteps - safely handle out of bounds
                    currentTurnPlayerId, // CurrentTurnPlayerId
                    currentTurnCardType, // CurrentTurnCardType
                    _discardPending.ToList(), // DiscardPendingPlayerIds
                    racerStatuses,
                    turnOrder, // Add turn order data
                    map,
                    _gameResults,
                    turnStartTime == default ? null : turnStartTime,
                    latestTurnLogs
                );
            });
        }
        #endregion

        private Task PublishEventsAsync(IEnumerable<IEvent> events, CancellationToken ct = default)
        {
            var cancellationToken = ct == default ? _cts.Token : ct;
            var tasks = events.Select(e =>
            {
                try
                {
                    return _events.PublishAsync(e, cancellationToken).AsTask();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error publishing event {EventType}", e.GetType().Name);
                    return Task.CompletedTask;
                }
            });
            return Task.WhenAll(tasks);
        }

        private async Task<TResult> WithGateAsync<TResult>(Func<List<IEvent>, TResult> body)
        {
            await _gate.WaitAsync(_cts.Token);
            var events = new List<IEvent>();
            TResult result;
            try
            {
                result = body(events);
            }
            finally { _gate.Release(); }

            if (events.Any())
                await PublishEventsAsync(events);

            return result;
        }

        private async Task<TResult> WithGateAsync<TResult>(Func<List<IEvent>, Task<TResult>> body)
        {
            await _gate.WaitAsync(_cts.Token);
            var events = new List<IEvent>();
            TResult result;
            try
            {
                result = await body(events);
            }
            finally { _gate.Release(); }

            if (events.Any())
                await PublishEventsAsync(events);

            return result;
        }

        private void AssignStartingPosition(Racer racer)
        {
            var startSegment = Map.Segments[0];
            var laneCount = startSegment.LaneCount;
            
            var racerCount = Racers.Count;
            
            var targetLaneIndex = racerCount % laneCount;
            var targetCellIndex = racerCount / laneCount;
            
            // Ensure we don't exceed segment boundaries
            if (targetCellIndex >= startSegment.CellCount)
            {
                targetCellIndex = startSegment.CellCount - 1;
            }
            
            racer.SegmentIndex = 0;
            racer.LaneIndex = targetLaneIndex;
            racer.CellIndex = targetCellIndex;
            racer.Score = targetCellIndex;
        }
    }
}
