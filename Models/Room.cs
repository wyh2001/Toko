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
        public ConcurrentDictionary<string, List<InstructionType>> SubmittedInstructions { get; set; }
            = new ConcurrentDictionary<string, List<InstructionType>>();
        public RaceMap? Map { get; set; }
    }
}
