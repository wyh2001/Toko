using MediatR;

namespace Toko.Models.Events
{
    public class StepAdvanced(string roomId, int round, int step) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public int Round { get; } = round;
        public int Step { get; } = step;
    }
}
