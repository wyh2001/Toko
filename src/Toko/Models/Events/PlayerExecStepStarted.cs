using MediatR;

namespace Toko.Models.Events
{
    public record PlayerExecStepStarted(string RoomId, int Round, int Step, string PlayerId) : IRoomEvent;
}