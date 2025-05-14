using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediatR;
using OneOf;
using Stateless;
using Toko.Models.Events;
using Toko.Services;
using static Toko.Services.RoomManager;

namespace Toko.Models
{
    /// <summary>
    /// 领域对象 Room – 封装所有业务规则：
    ///   • 超过 20 秒才允许其他玩家发起踢票；<br/>
    ///   • 不自动跳过；若无人踢，该玩家可继续操作；<br/>
    ///   • 每次成功提交获得 BANK_INCREMENT 时间奖励；<br/>
    ///   • 返回类型、错误枚举与 RoomManager 保持一致。
    /// </summary>
    public class Room
    {
        #region ▶ 公共属性
        public string Id { get; } = Guid.NewGuid().ToString();
        public string? Name { get; set; }
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; }
        public RaceMap Map { get; set; } = RaceMapFactory.CreateDefaultMap();
        public List<Racer> Racers { get; } = new();

        public int CurrentRound { get; private set; }
        public int CurrentStep { get; private set; }
        public RoomStatus Status => _gameSM.State;
        #endregion

        #region ▶ FSM
        private enum Phase { CollectingCards, CollectingParams }
        public enum RoomStatus { Waiting, Playing, Finished }
        private enum GameTrigger { Start, GameOver }
        private enum PhaseTrigger { CardsReady, StepDone, Timeout }

        private readonly StateMachine<RoomStatus, GameTrigger> _gameSM;
        private readonly StateMachine<Phase, PhaseTrigger> _phaseSM;
        #endregion

        #region ▶ 常量 & 字段
        private readonly List<int> _steps;
        private readonly List<string> _order = new();
        private int _idx;                                 // 当前顺序索引

        private readonly IMediator _mediator;
        private CancellationTokenSource? _cts;             // 单房计时器

        private readonly Dictionary<string, DateTime> _thinkStart = new();
        private readonly Dictionary<string, TimeSpan> _bank = new();
        private readonly HashSet<string> _banned = new();
        private readonly Dictionary<string, HashSet<string>> _kickVotes = new();
        private readonly Dictionary<string, string> _cardNow = new();

        private static readonly TimeSpan INIT_BANK = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BANK_INCREMENT = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan TIMEOUT_PROMPT = TimeSpan.FromSeconds(30); // UI 参考
        private static readonly TimeSpan KICK_ELIGIBLE = TimeSpan.FromSeconds(20);
        #endregion

        public Room(IMediator mediator, IEnumerable<int> stepsPerRound)
        {
            _mediator = mediator;
            _steps = stepsPerRound.Any() ? stepsPerRound.ToList() : new() { 5 };
            _gameSM = new(RoomStatus.Waiting);
            _phaseSM = new(Phase.CollectingCards);
            ConfigureFSM();
        }

        #region ▶ 游戏控制
        public OneOf<StartRoomSuccess, StartRoomError> StartGame()
        {
            if (Status == RoomStatus.Playing) return StartRoomError.AlreadyStarted;
            if (Status == RoomStatus.Finished) return StartRoomError.AlreadyFinished;
            if (!Racers.Any()) return StartRoomError.NoPlayers;

            _order.Clear();
            _order.AddRange(Racers.Select(r => r.Id));
            foreach (var pid in _order) _bank[pid] = INIT_BANK;
            CurrentRound = 0; CurrentStep = 0;
            _gameSM.Fire(GameTrigger.Start);
            return new StartRoomSuccess(Id);
        }

        public void EndGame() => _gameSM.Fire(GameTrigger.GameOver);
        #endregion

        #region ▶ 卡片提交
        public OneOf<SubmitStepCardSuccess, SubmitStepCardError> SubmitStepCard(string pid, string cardId)
        {
            // 1. 基础校验
            if (_banned.Contains(pid)) return SubmitStepCardError.PlayerBanned;
            if (_phaseSM.State != Phase.CollectingCards) return SubmitStepCardError.WrongPhase;
            if (pid != _order[_idx]) return SubmitStepCardError.NotYourTurn;

            var racer = Racers.FirstOrDefault(r => r.Id == pid);
            if (racer is null) return SubmitStepCardError.PlayerNotFound;
            var cardObj = racer.Hand.FirstOrDefault(c => c.Id == cardId);
            if (cardObj is null) return SubmitStepCardError.CardNotFound;

            // 2. 更新时间银行
            UpdateBank(pid);

            // 3. 把卡牌从手牌移入弃牌堆，避免后续再次提交同一张
            racer.Hand.Remove(cardObj);
            racer.DiscardPile.Add(cardObj);

            // 4. 记录提交并推进顺序
            _cardNow[pid] = cardId;
            MoveNextPlayer();
            return new SubmitStepCardSuccess(cardId);
        }
        #endregion

        #region ▶ 参数提交
        public OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError> SubmitExecutionParam(string pid, ExecParameter p)
        {
            if (_banned.Contains(pid)) return SubmitExecutionParamError.PlayerBanned;
            if (_phaseSM.State != Phase.CollectingParams) return SubmitExecutionParamError.WrongPhase;
            if (pid != _order[_idx]) return SubmitExecutionParamError.NotYourTurn;
            var racer = Racers.FirstOrDefault(r => r.Id == pid) ??
                        throw new InvalidOperationException();
            if (!_cardNow.TryGetValue(pid, out var cardId)) return SubmitExecutionParamError.CardNotFound;
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

        #region ▶ 投票踢人（单票生效）
        public enum KickVoteError { WrongPhase, TooEarly, TargetNotFound, AlreadyKicked }
        public record KickVoteSuccess(string TargetId);

        public OneOf<KickVoteSuccess, KickVoteError> VoteKick(string voterId, string targetId)
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

        #region ▶ 内部帮助
        void ConfigureFSM()
        {
            _gameSM.Configure(RoomStatus.Waiting)
                   .Permit(GameTrigger.Start, RoomStatus.Playing);
            _gameSM.Configure(RoomStatus.Playing)
                   .OnEntry(() => _phaseSM.Activate())
                   .Permit(GameTrigger.GameOver, RoomStatus.Finished);
            _gameSM.Configure(RoomStatus.Finished)
                   .OnEntry(() => _cts?.Cancel());

            _phaseSM.Configure(Phase.CollectingCards)
                    .OnEntry(() => { _cardNow.Clear(); _idx = 0; PromptCard(_order[0]); })
                    .Permit(PhaseTrigger.CardsReady, Phase.CollectingParams)
                    .Permit(PhaseTrigger.Timeout, Phase.CollectingParams);
            _phaseSM.Configure(Phase.CollectingParams)
                    .OnEntry(() => { _idx = 0; PromptParam(_order[0]); })
                    .Permit(PhaseTrigger.StepDone, Phase.CollectingCards)
                    .Permit(PhaseTrigger.Timeout, Phase.CollectingCards);
            _phaseSM.OnTransitioned(t => { if (t.Trigger == PhaseTrigger.StepDone) NextStep(); });
        }

        void PromptCard(string pid) { _thinkStart[pid] = DateTime.UtcNow; RescheduleTimer(); _mediator.Publish(new PlayerCardSubmissionStarted(Id, CurrentRound, CurrentStep, pid)); }
        void PromptParam(string pid) { _thinkStart[pid] = DateTime.UtcNow; RescheduleTimer(); _mediator.Publish(new PlayerParameterSubmissionStarted(Id, CurrentRound, CurrentStep, pid)); }

        void RescheduleTimer()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ = Task.Delay(TIMEOUT_PROMPT, _cts.Token).ContinueWith(_ =>
            {
                if (_cts.IsCancellationRequested || Status != RoomStatus.Playing) return;
                var pid = _order[_idx];
                if (IsKickEligible(pid))
                    _mediator.Publish(new PlayerTimeoutElapsed(Id, pid)); // 提醒 UI 可踢
            }, TaskScheduler.Default);
        }

        bool Validate(CardType t, ExecParameter p) => t switch
        {
            CardType.Move => p.Effect is >= 0 and <= 2,
            CardType.ChangeLane => p.Effect is 1 or -1,
            CardType.Repair => p.DiscardedCardIds?.Any() == true,
            _ => false
        };

        void UpdateBank(string pid)
        {
            var elapsed = DateTime.UtcNow - _thinkStart[pid];
            _bank[pid] -= elapsed; _bank[pid] += BANK_INCREMENT;
            if (_bank[pid] < TimeSpan.Zero) _bank[pid] = TimeSpan.Zero;
        }

        bool IsKickEligible(string pid) => DateTime.UtcNow - _thinkStart[pid] >= KICK_ELIGIBLE;

        void MoveNextPlayer()
        {
            do { _idx++; } while (_idx < _order.Count && _banned.Contains(_order[_idx]));
            if (_idx == _order.Count) _phaseSM.Fire(PhaseTrigger.CardsReady); // or StepDone depending phase
            else if (_phaseSM.State == Phase.CollectingCards) PromptCard(_order[_idx]);
            else PromptParam(_order[_idx]);
        }

        void NextStep() { if (++CurrentStep >= _steps[CurrentRound]) { CurrentStep = 0; if (++CurrentRound >= _steps.Count) _gameSM.Fire(GameTrigger.GameOver); } }
        #endregion
    }
}
