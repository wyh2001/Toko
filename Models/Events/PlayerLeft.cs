using MediatR;

namespace Toko.Models.Events
{
    public class PlayerLeft(string roomId, string playerId, string playerName) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public string PlayerId { get; } = playerId;
        public string PlayerName { get; } = playerName;
    }
}
