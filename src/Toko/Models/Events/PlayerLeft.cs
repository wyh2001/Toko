using MediatR;

namespace Toko.Models.Events
{
    public record PlayerLeft(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
}
