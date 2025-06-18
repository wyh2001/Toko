using MediatR;

namespace Toko.Models.Events
{
    public record PlayerJoined(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
}
