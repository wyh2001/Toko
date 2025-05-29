using MediatR;

namespace Toko.Models.Events
{
    public class PlayerStepExecuted(string roomId, int round, int step, List<TurnLog> logs) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public int Round { get; } = round;
        public int Step { get; } = step;
        public List<TurnLog> Logs { get; } = logs;
    }
}
