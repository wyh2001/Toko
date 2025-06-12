using MediatR;
using static Toko.Models.Room;

namespace Toko.Models.Events
{
    public class PhaseChanged(string roomId, Room.Phase phase, int round, int step) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public Phase Phase { get; } = phase;
        public int Round { get; } = round;
        public int Step { get; } = step;
    }
}
