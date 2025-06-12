using MediatR;

namespace Toko.Models.Events
{
    public class PlayerReadyToggled(string roomId, string playerId, bool isReady) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public string PlayerId { get; } = playerId;
        public bool IsReady { get; } = isReady;
    }
}
