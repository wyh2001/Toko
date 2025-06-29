using MediatR;

namespace Toko.Models.Events
{
    public record PlayerCardSubmitted(string RoomId, int Round, int Step, string PlayerId, string CardId) : IRoomEvent;
}
