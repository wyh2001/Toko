namespace Toko.Models
{
    public class TurnLog
    {
        public required string PlayerId { get; set; }
        public required ConcreteInstruction Instruction { get; set; }
        public int SegmentIndex { get; set; } // Added
        public int LaneIndex { get; set; }    // Added
    }
}