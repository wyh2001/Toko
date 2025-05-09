namespace Toko.Models
{
    public class TurnLog
    {
        public string PlayerId { get; set; }
        public ConcreteInstruction Instruction { get; set; }
        public int SegmentIndex { get; set; } // Added
        public int LaneIndex { get; set; }    // Added
    }
}