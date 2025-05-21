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

namespace Toko.Models
{
    public class Room : IDisposable
    {
        #region ▶ 公共属性
        public string Id { get; } = Guid.NewGuid().ToString();
        public string? Name { get; set; }
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; }
        public RaceMap Map { get; set; } = RaceMapFactory.CreateDefaultMap();
        public List<Racer> Racers { get; } = new();
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

        #region ▶ 字段 & 常量
        private readonly List<int> _steps;
        private readonly List<string> _order = new();
        private int _idx;
        private int _stepInRound;

        private readonly IMediator _mediator;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _timerLock = new();
        private Timer? _promptTimer;

        private readonly Dictionary<string, DateTime> _thinkStart = new();
        private readonly Dictionary<string, TimeSpan> _bank = new();
        private readonly HashSet<string> _banned = new();
        private readonly Dictionary<(string, int, int), string> _cardNow = new(); // (pid, round, step) -> cardId
        private HashSet<string> _discardPending = new();

        private static readonly TimeSpan INIT_BANK = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BANK_INCREMENT = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan TIMEOUT_PROMPT = TimeSpan.FromSeconds(5);   // 轮询频率
        private static readonly TimeSpan KICK_ELIGIBLE = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan DISCARD_AUTO_SKIP = TimeSpan.FromSeconds(15);

        private const int AUTO_DRAW = 5;   // 每回合自动抽 5 张
        private const int DRAW_ON_SKIP = 1;
        private const string SKIP = "SKIP"; // 代表跳过抽卡

        private bool _disposed;
        private readonly ILogger<Room> _log;
        #endregion

        public Room(IMediator mediator, IEnumerable<int> stepsPerRound, ILogger<Room> log)
        {
            _mediator = mediator;
            _steps = stepsPerRound.Any() ? stepsPerRound.ToList() : new() { 5 };
            _gameSM = new(RoomStatus.Waiting);
            _phaseSM = new(Phase.CollectingCards);
            ConfigureFSM();
            _log = log;
        }

        #region ▶ 游戏控制
        public async Task<OneOf<StartRoomSuccess, StartRoomError>> StartGameAsync(string playerId)
        {
            await _gate.WaitAsync();
            try
            {
                if (Status == RoomStatus.Playing) return StartRoomError.AlreadyStarted;
                if (Status == RoomStatus.Finished) return StartRoomError.AlreadyFinished;
                if (!Racers.Any()) return StartRoomError.NoPlayers;
                var host = Racers.FirstOrDefault(r => r.Id == playerId);
                if (host?.IsHost != true) return StartRoomError.NotHost;
                if (this.Racers.Any(r => !r.IsReady)) return StartRoomError.NotAllReady;

                _order.Clear();
                _order.AddRange(Racers.Select(r => r.Id));
                foreach (var id in _order) _bank[id] = INIT_BANK;

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

        //public record JoinRoomSuccess(Racer Racer);
        //public enum JoinRoomError { RoomFull }
        public async Task<OneOf<JoinRoomSuccess, JoinRoomError>> JoinRoomAsync(string playerId, string playerName)
        {
            await _gate.WaitAsync();
            try
            {
                if (Racers.Count >= MaxPlayers) return JoinRoomError.RoomFull;
                var racer = new Racer { Id = playerId, PlayerName = playerName };
                InitializeDeck(racer); DrawCardsInternal(racer, 5);
                Racers.Add(racer);
                return new JoinRoomSuccess(racer);
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
                return new LeaveRoomSuccess(pid);
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
                return new ReadyUpSuccess(pid, ready);
            }
            finally { _gate.Release(); }
        }
        #endregion

        #region ▶ 收卡
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
            return new SubmitStepCardSuccess(cardId);
        }
        #endregion

        #region ▶ 收参
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
            new TurnExecutor(Map).ApplyInstruction(racer, ins, this);
            _mediator.Publish(new PlayerStepExecuted(Id, CurrentRound, CurrentStep, new()));

            MoveNextPlayer();
            return new SubmitExecutionParamSuccess(ins);
        }
        #endregion

        #region ▶ 并发弃牌
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

            new TurnExecutor(Map).DiscardCards(racer, cardIds);
            _mediator.Publish(new PlayerDiscardExecuted(Id, CurrentRound, CurrentStep, pid, cardIds));

            _discardPending.Remove(pid);
            UpdateBank(pid);
            RescheduleTimer();

            if (_discardPending.Count == 0)
                _phaseSM.Fire(PhaseTrigger.DiscardDone);

            return new DiscardCardsSuccess(cardIds);
        }
        #endregion

        #region ▶ 投票踢人
        public async Task<OneOf<KickVoteSuccess, KickVoteError>> VoteKickAsync(string voterId, string targetId)
        {
            await _gate.WaitAsync();
            try { return VoteKick(voterId, targetId); }
            finally { _gate.Release(); }
        }

        public enum KickVoteError { WrongPhase, TooEarly, TargetNotFound, AlreadyKicked }
        public record KickVoteSuccess(string TargetId);

        private OneOf<KickVoteSuccess, KickVoteError> VoteKick(string voterId, string targetId)
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

        #region ▶ FSM 配置
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
        #endregion

        #region ▶ 各阶段启动
        void StartCardCollection()
        {
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
            }
            RescheduleTimer();
        }
        #endregion

        #region ▶ Prompt & 计时器
        void PromptCard(string pid)
        {
            _thinkStart[pid] = DateTime.UtcNow;
            RescheduleTimer();
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
            RescheduleTimer();
            _mediator.Publish(new PlayerParameterSubmissionStarted(Id, CurrentRound, CurrentStep, pid));
        }

        private void RescheduleTimer()
        {
            lock (_timerLock)
            {
                if (_promptTimer is null)
                {
                    _promptTimer = new Timer(_ => OnPromptTimer(), null, TIMEOUT_PROMPT, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    _promptTimer.Change(TIMEOUT_PROMPT, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private async void OnPromptTimer()
        {
            // 单房间串行：拿锁后执行，确保与其他 API 不并发
            await _gate.WaitAsync();
            try
            {
                if (Status != RoomStatus.Playing)
                    return;   // 游戏已结束或未开始

                if (_phaseSM.State == Phase.Discarding)
                {
                    foreach (var pid in _discardPending.ToList())
                    {
                        var elapsed = DateTime.UtcNow - _thinkStart[pid];
                        if (elapsed >= DISCARD_AUTO_SKIP)
                        {
                            _discardPending.Remove(pid);
                            await _mediator.Publish(new PlayerDiscardExecuted(Id, CurrentRound, CurrentStep, pid, new List<string>()));
                        }
                        else if (elapsed >= KICK_ELIGIBLE)
                        {
                            await _mediator.Publish(new PlayerTimeoutElapsed(Id, pid));
                        }
                    }

                    if (_discardPending.Count == 0)
                    {
                        _phaseSM.Fire(PhaseTrigger.DiscardDone);
                        return; // 由状态机中的逻辑决定是否再次调度
                    }
                }
                else
                {
                    var pid = _order[_idx];
                    if (IsKickEligible(pid))
                        await _mediator.Publish(new PlayerTimeoutElapsed(Id, pid));
                }
            }
            catch (Exception ex)
            {
                // 处理异常
                //Console.WriteLine($"Error in OnPromptTimer: {ex.Message}");
                _log.LogError(ex, "Error in OnPromptTimer");
            }
            finally
            {
                _gate.Release();
                // 无论如何都重新定时
                RescheduleTimer();
            }
        }
        #endregion

        #region ▶ 工具
        static bool Validate(CardType t, ExecParameter p) => t switch
        {
            CardType.Move => p.Effect is 1 or 2,
            CardType.ChangeLane => p.Effect is 1 or -1,
            CardType.Repair => p.DiscardedCardIds?.Any() == true,
            _ => false
        };

        void UpdateBank(string pid)
        {
            var elapsed = DateTime.UtcNow - _thinkStart[pid];
            _bank[pid] -= elapsed;
            _bank[pid] += BANK_INCREMENT;
            if (_bank[pid] < TimeSpan.Zero) _bank[pid] = TimeSpan.Zero;
        }

        bool IsKickEligible(string pid) => DateTime.UtcNow - _thinkStart[pid] >= KICK_ELIGIBLE;

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
            return new DrawSkipSuccess(result);
        }

        public List<Card> DrawCards(Racer racer, int requestedCount)
        {
            int space = racer.HandCapacity - racer.Hand.Count;
            int toDraw = Math.Min(requestedCount, Math.Max(0, space));
            var drawn = DrawCardsInternal(racer, toDraw);
            return drawn;
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_timerLock)
            {
                _promptTimer?.Dispose();
                _promptTimer = null;
            }
            _gate.Dispose();
            _disposed = true;
        }
        #endregion
    }
}
