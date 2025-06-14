using MediatR;

namespace Toko.Models.Events
{
    public class PlayerParameterSubmissionStarted(string roomId, int round, int step, string playerId) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public int Round { get; } = round;
        public int Step { get; } = step;
        public string PlayerId { get; } = playerId;
    }

}