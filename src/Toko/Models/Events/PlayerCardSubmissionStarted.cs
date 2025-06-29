using MediatR;

namespace Toko.Models.Events
{
    public record PlayerCardSubmissionStarted(string RoomId, int Round, int Step, string PlayerId) : IRoomEvent;
}
