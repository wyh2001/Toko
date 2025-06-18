using MediatR;

namespace Toko.Models.Events
{
    public record StepAdvanced(string RoomId, int Round, int Step) : IRoomEvent;
}
