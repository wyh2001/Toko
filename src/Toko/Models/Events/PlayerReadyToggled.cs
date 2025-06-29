using MediatR;

namespace Toko.Models.Events
{
    public record PlayerReadyToggled(string RoomId, string PlayerId, bool IsReady) : IRoomEvent;
}
