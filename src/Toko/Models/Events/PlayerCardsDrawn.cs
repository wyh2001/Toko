using MediatR;

namespace Toko.Models.Events
{
    public record PlayerCardsDrawn(string RoomId, int Round, int Step, string PlayerId, int HandCount) : IRoomEvent;
}
