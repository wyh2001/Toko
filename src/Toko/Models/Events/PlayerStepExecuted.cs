using MediatR;

namespace Toko.Models.Events
{
    public record PlayerStepExecuted(string RoomId, int Round, int Step) : IRoomEvent;
}
