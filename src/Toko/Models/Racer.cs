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
        public int SegmentIndex { get; set; }   // Current segment index
        public int LaneIndex { get; set; }      // Current lane 0..LaneCount-1
        public int CellIndex { get; set; }      // Position inside segment: 0 to LaneCells[LaneIndex].Count-1
        public int Score { get; set; } = 0;
        public int Gear { get; set; } = 1; // Current gear of the racer (1-6)
        public bool IsHost { get; set; } = false; // Is room host
        public bool IsReady { get; set; } = false;
    }
}
