//public enum Direction { Up, Right, Down, Left }

namespace Toko.Models
{
    public class Racer
    {
        public required string Id { get; set; }
        public required string PlayerName { get; set; }
        public Queue<Card> Deck { get; set; } = new Queue<Card>();
        public List<Card> Hand { get; set; } = new List<Card>();
        public List<Card> DiscardPile { get; set; } = new List<Card>();
        public int HandCapacity { get; set; } = 5;
        public int SegmentIndex { get; set; }   // 当前段索引
        public int LaneIndex { get; set; }      // 当前车道 0..LaneCount-1
        public int CellIndex { get; set; }      // 段内格子上的位置：0 到 LaneCells[LaneIndex].Count-1
        public int Score { get; set; } = 0;
        public bool IsHost { get; set; } = false; // 是否是房主
        public bool IsReady { get; set; } = false;
    }
}
