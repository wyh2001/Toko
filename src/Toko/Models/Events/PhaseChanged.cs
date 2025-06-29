using MediatR;
using static Toko.Models.Room;

namespace Toko.Models.Events
{
    public record PhaseChanged(string RoomId, Phase Phase, int Round, int Step) : IRoomEvent;
}
