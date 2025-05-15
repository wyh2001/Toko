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

        private readonly IMediator _mediator;
        private CancellationTokenSource? _cts;

        private readonly Dictionary<string, DateTime> _thinkStart = new();
        private readonly Dictionary<string, TimeSpan> _bank = new();
        private readonly HashSet<string> _banned = new();
        private readonly Dictionary<string, string> _cardNow = new();
        private HashSet<string> _discardPending = new();

        private static readonly TimeSpan INIT_BANK = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BANK_INCREMENT = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan TIMEOUT_PROMPT = TimeSpan.FromSeconds(5);   // 轮询频率
        private static readonly TimeSpan KICK_ELIGIBLE = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan DISCARD_AUTO_SKIP = TimeSpan.FromSeconds(15);

        private const int AUTO_DRAW = 2;   // 每回合自动抽 2 张
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
            foreach (var id in _order) _bank[id] = INIT_BANK;

            CurrentRound = 0; CurrentStep = 0;
            _gameSM.Fire(GameTrigger.Start);
            return new StartRoomSuccess(Id);
        }

        public void EndGame() => _gameSM.Fire(GameTrigger.GameOver);
        #endregion

        #region ▶ 收卡
        public OneOf<SubmitStepCardSuccess, SubmitStepCardError>
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

            _cardNow[pid] = cardId;
            MoveNextPlayer();
            return new SubmitStepCardSuccess(cardId);
        }
        #endregion

        #region ▶ 收参
        public OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>
            SubmitExecutionParam(string pid, ExecParameter p)
        {
            if (_banned.Contains(pid)) return SubmitExecutionParamError.PlayerBanned;
            if (_phaseSM.State != Phase.CollectingParams)
                return SubmitExecutionParamError.WrongPhase;
            if (pid != _order[_idx]) return SubmitExecutionParamError.NotYourTurn;

            var racer = Racers.First(r => r.Id == pid);
            if (!_cardNow.TryGetValue(pid, out var cardId))
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
        public OneOf<DiscardCardsSuccess, DiscardCardsError>
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

            new TurnExecutor(Map).DiscardCards(racer, cardIds, this);
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

        #region ▶ FSM 配置
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
            foreach (var r in Racers) AutoDraw(r, AUTO_DRAW);
            _cardNow.Clear();
            _idx = 0;
            PromptCard(_order[0]);
        }

        void StartParamCollection()
        {
            _idx = 0;
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
            _thinkStart[pid] = DateTime.UtcNow;
            RescheduleTimer();
            _mediator.Publish(new PlayerParameterSubmissionStarted(Id, CurrentRound, CurrentStep, pid));
        }

        void RescheduleTimer()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ = Task.Delay(TIMEOUT_PROMPT, _cts.Token).ContinueWith(_ =>
            {
                if (_cts.IsCancellationRequested || Status != RoomStatus.Playing) return;

                if (_phaseSM.State == Phase.Discarding)
                {
                    foreach (var pid in _discardPending.ToList())
                    {
                        var elapsed = DateTime.UtcNow - _thinkStart[pid];
                        if (elapsed >= DISCARD_AUTO_SKIP)
                        {
                            // 自动空弃
                            _discardPending.Remove(pid);
                            _mediator.Publish(new PlayerDiscardExecuted(Id, CurrentRound, CurrentStep, pid, new List<string>()));
                        }
                        else if (elapsed >= KICK_ELIGIBLE)
                        {
                            _mediator.Publish(new PlayerTimeoutElapsed(Id, pid));
                        }
                    }
                    if (_discardPending.Count == 0)
                    {
                        _phaseSM.Fire(PhaseTrigger.DiscardDone);
                        return;
                    }
                }
                else
                {
                    var pid = _order[_idx];
                    if (IsKickEligible(pid))
                        _mediator.Publish(new PlayerTimeoutElapsed(Id, pid));
                }

                RescheduleTimer();   // 继续下一个 5 s tick
            }, TaskScheduler.Default);
        }
        #endregion

        #region ▶ 工具
        void AutoDraw(Racer racer, int requested)
        {
            int space = racer.HandCapacity - racer.Hand.Count;
            int toDraw = Math.Min(requested, Math.Max(0, space));

            for (int i = 0; i < toDraw; i++)
            {
                if (!racer.Deck.Any())
                {
                    foreach (var c in racer.DiscardPile) racer.Deck.Enqueue(c);
                    racer.DiscardPile.Clear();
                }
                if (!racer.Deck.Any()) break;
                racer.Hand.Add(racer.Deck.Dequeue());
            }
        }

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
                var next = _phaseSM.State == Phase.CollectingCards
                           ? PhaseTrigger.CardsReady
                           : PhaseTrigger.ParamsDone;
                _phaseSM.Fire(next);
                return;
            }

            if (_phaseSM.State == Phase.CollectingCards) PromptCard(_order[_idx]);
            else if (_phaseSM.State == Phase.CollectingParams) PromptParam(_order[_idx]);
        }

        void NextStep()
        {
            if (++CurrentStep >= _steps[CurrentRound])
            {
                CurrentStep = 0;
                if (++CurrentRound >= _steps.Count)
                    _gameSM.Fire(GameTrigger.GameOver);
            }
        }
        #endregion
    }
}
