using System.Collections.Concurrent;
using MediatR;
using Stateless;
using Toko.Models.Events;
using Toko.Services;

namespace Toko.Models
{
    public class Room
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? Name { get; set; }
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; } = false;
        public List<Racer> Racers { get; set; } = new();
        public RaceMap? Map { get; set; }
        public int CurrentRound { get; set; } = 0;
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;
        /// <summary>当前正在收集第几步（从 0 开始）</summary>
        public int CurrentStep { get; set; } = 0;
        public DateTime PlayerDeadlineUtc { get; set; }

        /// <summary>
        /// 第 step 步各玩家交的卡片 ID; playerId → 卡片 ID 列表
        /// </summary>
        public ConcurrentDictionary<int, ConcurrentDictionary<string, string>>
            StepCardSubmissions
        { get; set; }
          = new ConcurrentDictionary<int, ConcurrentDictionary<string, string>>();

        /// <summary>
        /// 第 step 步已执行的指令（执行阶段才用到）
        /// </summary>
        public ConcurrentDictionary<int, ConcurrentDictionary<string, ConcreteInstruction>>
            StepExecSubmissions
        { get; set; }
          = new ConcurrentDictionary<int, ConcurrentDictionary<string, ConcreteInstruction>>();


        /// <summary>总共多少轮</summary>
        public int TotalRounds { get; set; } = 5;

        /// <summary>每一轮的步数，可以不一样。长度应该 = TotalRounds</summary>
        public List<int> StepsPerRound { get; set; } = new();

        public int CurrentRacerIndex { get; set; } = 0;
        public List<string> PlayerOrder { get; private set; } = new();

        public StateMachine<RoomStatus, Trigger> MainSM { get; private set; }
        public StateMachine<PlayingPhase, Trigger> SubSM { get; private set; }
        public StateMachine<string, CollectTrigger> CollectSM { get; private set; }

        private readonly IMediator _mediator;

        public Room(IMediator mediator)
        {
            MainSM = new StateMachine<RoomStatus, Trigger>(RoomStatus.Waiting);
            SubSM = new StateMachine<PlayingPhase, Trigger>(PlayingPhase.Collecting);
            CollectSM = new StateMachine<string, CollectTrigger>("Idle");

            ConfigureMain();
            ConfigureSub();
            this._mediator = mediator;
        }

        void ConfigureMain()
        {
            MainSM.Configure(RoomStatus.Waiting)
                  .Permit(Trigger.Start, RoomStatus.Playing);

            MainSM.Configure(RoomStatus.Playing)
                  .OnEntry(() => SubSM.Fire(Trigger.Timeout)) // enter collecting
                  .Permit(Trigger.Finish, RoomStatus.Finished);

            MainSM.Configure(RoomStatus.Finished);
        }

        void ConfigureSub()
        {
            SubSM.Configure(PlayingPhase.Collecting)
                 .OnEntry(SetupCollectSM)
                 .Permit(Trigger.AllPlayersDone, PlayingPhase.Executing);

            SubSM.Configure(PlayingPhase.Executing)
                 .OnEntry(OnEnterExecuting)
                 .Permit(Trigger.ExecutionDone, PlayingPhase.Collecting);

            SubSM.OnTransitioned(t =>
            {
                if (t.Trigger == Trigger.ExecutionDone)
                    AdvancePointer();
            });
        }

        // Build per‑player collect machine each step/round
        void SetupCollectSM()
        {
            CollectSM = new StateMachine<string, CollectTrigger>("Idle");

            // Build PlayerOrder from Racers list if mismatch
            if (PlayerOrder.Count != Racers.Count)
                PlayerOrder = Racers.Select(r => r.Id).ToList();

            CollectSM.Configure("Idle")
                     .Permit(CollectTrigger.Reset, StateOfPlayer(PlayerOrder[0]));

            for (int i = 0; i < PlayerOrder.Count; i++)
            {
                var pid = PlayerOrder[i];
                var curr = StateOfPlayer(pid);
                var next = (i == PlayerOrder.Count - 1) ? "Finished" : StateOfPlayer(PlayerOrder[i + 1]);

                CollectSM.Configure(curr)
                         .OnEntry(() => AskPlayerSubmit(pid))
                         .Permit(CollectTrigger.Submit, next)
                         .Permit(CollectTrigger.PlayerTimeout, next);
            }

            CollectSM.Configure("Finished")
                     .OnEntry(() => SubSM.Fire(Trigger.AllPlayersDone));

            CollectSM.Fire(CollectTrigger.Reset);
        }

        static string StateOfPlayer(string pid) => $"P:{pid}";

        void AskPlayerSubmit(string pid)
        {
            Console.WriteLine($"[Room {Id}] Ask {pid} R{CurrentRound}-S{CurrentStep}");
            // TODO: notify player to submit
            _mediator.Publish(new PlayerSubmissionStepStarted(Id, CurrentRound, CurrentStep, pid));
            PlayerDeadlineUtc = DateTime.UtcNow + TurnScheduler.TIMEOUT;
        }

        void OnEnterExecuting()
        {
            Console.WriteLine($"[Room {Id}] Executing R{CurrentRound}-S{CurrentStep}");
            // TODO: execute instructions

            CollectSM.Fire(CollectTrigger.Reset); // prep next step
            SubSM.Fire(Trigger.ExecutionDone);
        }

        void AdvancePointer()
        {
            if (++CurrentStep >= StepsPerRound[CurrentRound - 1])
            {
                CurrentStep = 0;
                if (++CurrentRound > TotalRounds)
                    MainSM.Fire(Trigger.Finish);
            }
        }

        public void ReceivePlayerSubmit(string playerId, string cardId)
        {
            if (CollectSM.State != StateOfPlayer(playerId)) return;
            StepCardSubmissions.GetOrAdd(CurrentStep, _ => new())[playerId] = cardId;
            CollectSM.Fire(CollectTrigger.Submit);
        }
    }
}
