namespace Toko.Shared.Models
{
    public class TurnLog(string message, string? playerId = null, int round = 0, int step = 0)
    {
        public string Message { get; set; } = message;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? PlayerId { get; set; } = playerId;
        public int Round { get; set; } = round;
        public int Step { get; set; } = step;
        public Guid Id { get; set; } = Guid.NewGuid();
        //public ConcreteInstruction? Instruction { get; set; } = instruction;
        //public int SegmentIndex { get; set; }
        //public int LaneIndex { get; set; }
    }
}