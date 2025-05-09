public enum Direction { Up, Right, Down, Left }

namespace Toko.Models
{
    public class Racer
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string PlayerName { get; set; }
        public Queue<Card> Deck { get; set; } = new Queue<Card>();
        public List<Card> Hand { get; set; } = new List<Card>();
        public List<Card> DiscardPile { get; set; } = new List<Card>();
        public int HandCapacity { get; set; } = 5;
        public int SegmentIndex { get; set; }   // 当前段索引
        public int LaneIndex { get; set; }      // 当前车道 0..LaneCount-1
    }
}
