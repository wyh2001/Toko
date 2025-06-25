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
        public enum Phase { CollectingCards, CollectingParams, Discarding }
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
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _steps = stepsPerRound?.Any() == true ? stepsPerRound.ToList() : new() { 5 };
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

        public enum GameEndReason
        {
            FinisherCrossedLine,
            NoActivePlayersLeft,
            TurnLimitReached,
        }
        public record PlayerResult(string PlayerId, int Rank, int Score);

        // Collects and returns the game results, ranking players by progress and assigning scores (total cells passed)
        private List<PlayerResult> CollectGameResults()
        {
            var segmentLengths = Map.SegmentLengths;

            // Compute progress for each player: total cells passed
            var progressList = Racers.Select(r => new
            {
                Racer = r,
                // Total cells passed (sum of lengths of previous segments + current cell index + 1)
                Score = segmentLengths.Take(r.SegmentIndex).Sum() + r.CellIndex + 1
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
                events.Add(new RoomEnded(Id, reason, results));
                return results;
            });
        }

        private List<PlayerResult> EndGame(GameEndReason reason)
        {
            _gameSM.Fire(GameTrigger.GameOver);
            List<PlayerResult> results = CollectGameResults();
            return results;
        }

        public async Task<OneOf<JoinRoomSuccess, JoinRoomError>> JoinRoomAsync(string playerId, string playerName)
        {
            return await WithGateAsync<OneOf<JoinRoomSuccess, JoinRoomError>>(events =>
            {
                // prevent from joining if already joined
                if (Racers.Any(r => r.Id == playerId)) return JoinRoomError.AlreadyJoined;
                if (Racers.Count >= MaxPlayers) return JoinRoomError.RoomFull;
                var racer = new Racer { Id = playerId, PlayerName = playerName };
                InitializeDeck(racer); DrawCardsInternal(racer, 5);
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

        public async Task<OneOf<ReadyUpSuccess, ReadyUpError>> ReadyUpAsync(string pid, bool ready)
        {
            return await WithGateAsync<OneOf<ReadyUpSuccess, ReadyUpError>>(events =>
            {
                var racer = Racers.FirstOrDefault(r => r.Id == pid);
                if (racer is null) return ReadyUpError.PlayerNotFound;
                racer.IsReady = ready;
                events.Add(new PlayerReadyToggled(Id, pid, ready));
                return new ReadyUpSuccess(this.Id, pid, ready);
            });
        }
        #endregion

        #region Collecting Cards
        public async Task<OneOf<SubmitStepCardSuccess, SubmitStepCardError>> SubmitStepCardAsync(string pid, string cardId)
        {
            return await WithGateAsync<OneOf<SubmitStepCardSuccess, SubmitStepCardError>>(events =>
            {
                if (_banned.Contains(pid)) return SubmitStepCardError.PlayerBanned;
                if (_phaseSM.State != Phase.CollectingCards)
                    return SubmitStepCardError.WrongPhase;
                if (pid != _order[_idx]) return SubmitStepCardError.NotYourTurn;

                var racer = Racers.FirstOrDefault(r => r.Id == pid)
                       ?? throw new InvalidOperationException();
                var cardObj = racer.Hand.FirstOrDefault(c => c.Id == cardId);
                if (cardObj is null) return SubmitStepCardError.CardNotFound;

                UpdateBank(pid, events);
                racer.Hand.Remove(cardObj);
                racer.DiscardPile.Add(cardObj);

                _cardNow[(pid, CurrentRound, _stepInRound)] = cardId;
                events.Add(new PlayerCardSubmitted(Id, CurrentRound, CurrentStep, pid, cardId));
                MoveNextPlayer(events);
                return new SubmitStepCardSuccess(this.Id, pid, cardId);
            });
        }
        #endregion

        #region Collecting Parameters
        public async Task<OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>> SubmitExecutionParamAsync(string pid, ExecParameter p)
        {
            return await WithGateAsync<OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>>(events =>
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

                UpdateBank(pid, events);

                var ins = new ConcreteInstruction { Type = card.Type, ExecParameter = p };
                var executionResult = _turnExecutor.ApplyInstruction(racer, ins, this);
                if (executionResult == TurnExecutor.TurnExecutionResult.PlayerFinished)
                {
                    events.Add(new PlayerFinished(Id, pid));
                    events.Add(new RoomEnded(Id, GameEndReason.FinisherCrossedLine, CollectGameResults()));
                    _gameSM.Fire(GameTrigger.GameOver);
                }
                events.Add(new PlayerStepExecuted(Id, CurrentRound, CurrentStep));
                MoveNextPlayer(events);
                return new SubmitExecutionParamSuccess(this.Id, pid, ins);
            });
        }
        #endregion

        #region Discarding Cards
        public async Task<OneOf<DiscardCardsSuccess, DiscardCardsError>> SubmitDiscardAsync(string pid, List<string> cardIds)
        {
            return await WithGateAsync<OneOf<DiscardCardsSuccess, DiscardCardsError>>(events =>
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
                events.Add(new PlayerDiscardExecuted(Id, CurrentRound, CurrentStep, pid, cardIds));

                _discardPending.Remove(pid);
                UpdateBank(pid, events);
                ResetPrompt(pid);

                if (_discardPending.Count == 0)
                    _phaseSM.Fire(PhaseTrigger.DiscardDone);

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
                    events.Add(new PlayerKicked(Id, kickedPlayerId));
                    return new KickPlayerSuccess(Id, playerId, kickedPlayerId);
                }
                if (Status != RoomStatus.Playing) return KickPlayerError.WrongPhase;
                if (!_order.Contains(kickedPlayerId)) return KickPlayerError.TargetNotFound;
                if (_banned.Contains(kickedPlayerId)) return KickPlayerError.AlreadyKicked;
                if (!IsKickEligible(kickedPlayerId)) return KickPlayerError.TooEarly;

                _banned.Add(kickedPlayerId);
                events.Add(new PlayerKicked(Id, kickedPlayerId));
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
                _steps = settings.StepsPerRound ?? _steps;
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
                await _mediator.Publish(new PhaseChanged(Id, t.Destination, CurrentRound, CurrentStep));
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
                await _mediator.Publish(new RoomEnded(Id, GameEndReason.NoActivePlayersLeft, results));
                return;
            }
            var eventsToPublish = new List<INotification>();
            foreach (var r in Racers)
            {
                DrawCards(r, AUTO_DRAW);
                eventsToPublish.Add(new PlayerCardsDrawn(Id, CurrentRound, CurrentStep, r.Id, r.Hand.Count));
            }
            await PublishEventsAsync(eventsToPublish);
            _cardNow.Clear();
            _idx = 0;
            _stepInRound = 0;
            await _mediator.Publish(PromptCard(_order[0]));
        }

        void StartParamCollection()
        {
            _idx = 0;
            _stepInRound = 0;
        }

        async Task StartDiscardPhaseAsync()
        {
            _idx = 0;
            _discardPending = _order.Where(id => !_banned.Contains(id))
                                     .ToHashSet();

            var eventsToPublish = new List<INotification>();
            foreach (var pid in _discardPending)
            {
                _thinkStart[pid] = DateTime.UtcNow;
                eventsToPublish.Add(new PlayerDiscardStarted(Id, CurrentRound, CurrentStep, pid));
                ResetPrompt(pid);
            }
            await PublishEventsAsync(eventsToPublish);
        }
        #endregion

        #region Helper Methods for Prompting Players
        PlayerCardSubmissionStarted PromptCard(string pid)
        {
            _thinkStart[pid] = DateTime.UtcNow;
            ResetPrompt(pid);
            return new PlayerCardSubmissionStarted(Id, CurrentRound, CurrentStep, pid);
        }

        void PromptParam(string pid, List<INotification> events)
        {
            //check if draw to skip
            if (_cardNow.TryGetValue((pid, CurrentRound, _stepInRound), out var cardId) && cardId == SKIP)
            {
                events.Add(new PlayerParameterSubmissionSkipped(Id, CurrentRound, CurrentStep, pid));
                MoveNextPlayer(events);
                return;
            }
            _thinkStart[pid] = DateTime.UtcNow;
            ResetPrompt(pid);
            events.Add(new PlayerParameterSubmissionStarted(Id, CurrentRound, CurrentStep, pid));
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

        void UpdateBank(string pid, List<INotification> events)
        {
            var elapsed = DateTime.UtcNow - _thinkStart[pid];
            _bank[pid] -= elapsed;
            _bank[pid] += BANK_INCREMENT;
            events.Add(new PlayerBankUpdated(Id, pid, _bank[pid]));

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
                    var events = new List<INotification>();
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
                                    events.Add(new PlayerDiscardExecuted(Id, CurrentRound, CurrentStep, pid, new List<string>()));
                                    events.Add(new PlayerBankUpdated(Id, pid, _bank[pid]));

                                    // if no more players pending discard, then move to next phase
                                    if (_discardPending.Count == 0)
                                        _phaseSM.Fire(PhaseTrigger.DiscardDone);
                                }
                                else if (IsTimeOut(pid))
                                {
                                    events.Add(new PlayerTimeoutElapsed(Id, pid));
                                }
                            }

                            continue;
                        }

                        // the phase of collecting cards or parameters
                        var pidTurn = _order[_idx]; // current turn player's id
                        if (IsTimeOut(pidTurn))
                        {
                            events.Add(new PlayerTimeoutElapsed(Id, pidTurn));
                            events.Add(new PlayerBankUpdated(Id, pidTurn, _bank[pidTurn]));

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

        bool IsKickEligible(string pid) => DateTime.UtcNow - _thinkStart[pid] >= KICK_ELIGIBLE;
        bool IsTimeOut(string pid) => DateTime.UtcNow >= _nextPrompt[pid];
        bool IsAnyActivePlayer() => _order.Any(pid => !_banned.Contains(pid));

        void MoveNextPlayer(List<INotification> events)
        {
            do { _idx++; } while (_idx < _order.Count && _banned.Contains(_order[_idx]));

            if (_idx == _order.Count)
            {
                _idx = 0;
                if (++_stepInRound >= _steps[CurrentRound])
                {
                    events.Add(new StepAdvanced(Id, CurrentRound, _stepInRound));
                    var trigger = _phaseSM.State == Phase.CollectingCards ? PhaseTrigger.CardsReady : PhaseTrigger.ParamsDone;
                    var oldState = _phaseSM.State;
                    _phaseSM.Fire(trigger);
                    var newState = _phaseSM.State;
                    if (oldState == Phase.CollectingCards && newState == Phase.CollectingParams)
                    {
                        PromptParam(_order[0], events);
                    }
                    return;
                }
                events.Add(new StepAdvanced(Id, CurrentRound, _stepInRound));
            }

            if (_phaseSM.State == Phase.CollectingCards) events.Add(PromptCard(_order[_idx]));
            else if (_phaseSM.State == Phase.CollectingParams) PromptParam(_order[_idx], events);
        }

        async Task NextStepAsync(CancellationToken ct = default)
        {
            _stepInRound = 0;
            if (++CurrentRound >= _steps.Count)
            {
                var results = EndGame(GameEndReason.TurnLimitReached);
                try
                {
                    await _mediator.Publish(new RoomEnded(Id, GameEndReason.TurnLimitReached, results), ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error publishing RoomEnded event for Room {RoomId}", Id);
                }
            }
            else
            {
                await _mediator.Publish(new RoundAdvanced(Id, CurrentRound), ct);
            }
        }

        public async Task<OneOf<DrawSkipSuccess, DrawSkipError>> DrawSkipAsync(string pid)
        {
            return await WithGateAsync<OneOf<DrawSkipSuccess, DrawSkipError>>(events =>
            {
                if (_banned.Contains(pid)) return DrawSkipError.PlayerBanned;
                if (_phaseSM.State != Phase.CollectingCards)
                    return DrawSkipError.WrongPhase;
                if (pid != _order[_idx]) return DrawSkipError.NotYourTurn;

                var racer = Racers.FirstOrDefault(r => r.Id == pid)
                       ?? throw new InvalidOperationException();
                UpdateBank(pid, events);
                var result = DrawCards(racer, DRAW_ON_SKIP);
                events.Add(new PlayerDrawToSkip(Id, CurrentRound, CurrentStep, pid));
                _cardNow[(pid, CurrentRound, _stepInRound)] = SKIP;
                MoveNextPlayer(events);
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
        // Snapshot DTO for API
        public record RoomStatusSnapshot(
            string RoomId,
            string Name,
            int MaxPlayers,
            bool IsPrivate,
            string Status,
            string Phase,
            int CurrentRound,
            int CurrentStep,
            string? CurrentTurnPlayerId,
            List<string> DiscardPendingPlayerIds,
            List<RacerStatus> Racers,
            object Map
        );
        public record RacerStatus(string Id, string Name, int Lane, int Tile, double Bank, bool IsHost, bool IsReady, int HandCount, bool IsBanned);

        // Returns a snapshot of the current room status for API
        public async Task<RoomStatusSnapshot> GetStatusSnapshotAsync()
        {
            return await WithGateAsync(events =>
            {
                var phase = _phaseSM.State.ToString();
                var racers = Racers.Select(r => new RacerStatus(
                    r.Id,
                    r.PlayerName,
                    r.LaneIndex,
                    r.CellIndex,
                    _bank.TryGetValue(r.Id, out var bank) ? Math.Round(bank.TotalSeconds, 2) : 0.0,
                    r.IsHost,
                    r.IsReady,
                    r.Hand.Count,
                    _banned.Contains(r.Id)
                )).ToList();
                var map = new
                {
                    Segments = Map.Segments.Select(seg => new
                    {
                        Type = seg.Type.ToString(),
                        LaneCount = seg.LaneCount,
                        LaneCellCounts = seg.LaneCells.Select(lane => lane.Count).ToList()
                    }).ToList()
                };
                return new RoomStatusSnapshot(
                    Id,
                    Name ?? string.Empty, // Ensure Name is not null
                    MaxPlayers,
                    IsPrivate,
                    Status.ToString(),
                    phase,
                    CurrentRound,
                    CurrentStep,
                    _order.ElementAtOrDefault(_idx), // CurrentTurnPlayerId
                    _discardPending.ToList(), // DiscardPendingPlayerIds
                    racers,
                    map
                );
            });
        }
        #endregion

        private Task PublishEventsAsync(IEnumerable<INotification> events, CancellationToken ct = default)
        {
            var cancellationToken = ct == default ? _cts.Token : ct;
            var tasks = events.Select(e =>
            {
                try
                {
                    return _mediator.Publish(e, cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error publishing event {EventType}", e.GetType().Name);
                    return Task.CompletedTask;
                }
            });
            return Task.WhenAll(tasks);
        }

        private async Task<TResult> WithGateAsync<TResult>(Func<List<INotification>, TResult> body)
        {
            await _gate.WaitAsync(_cts.Token);
            var events = new List<INotification>();
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
    }
}
