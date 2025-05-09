using System.Collections.Concurrent;

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
        public int CurrentTurn { get; set; } = 0;
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;
        /// <summary>当前正在收集第几步（从 0 开始）</summary>
        public int CollectingStep { get; set; } = 0;

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

    }
}
