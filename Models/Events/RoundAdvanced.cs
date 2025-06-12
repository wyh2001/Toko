using MediatR;

namespace Toko.Models.Events
{
    public class RoundAdvanced(string roomId, int round) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public int Round { get; } = round;
    }
}
