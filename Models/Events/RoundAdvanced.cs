using MediatR;

namespace Toko.Models.Events
{
    public record RoundAdvanced(string RoomId, int Round) : IRoomEvent;
}
