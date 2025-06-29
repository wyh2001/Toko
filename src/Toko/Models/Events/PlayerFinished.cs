using MediatR;

namespace Toko.Models.Events
{
    public record PlayerFinished(string RoomId, string PlayerId) : IRoomEvent;
}