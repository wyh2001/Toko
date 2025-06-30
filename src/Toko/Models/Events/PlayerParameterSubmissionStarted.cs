using MediatR;

namespace Toko.Models.Events
{
    public record PlayerParameterSubmissionStarted(string RoomId, int Round, int Step, string PlayerId, CardType CardType) : IRoomEvent;
}