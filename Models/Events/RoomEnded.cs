using static Toko.Models.Room;

namespace Toko.Models.Events
{
    public record RoomEnded(string RoomId, GameEndReason Reason, List<PlayerResult> Results) : IRoomEvent;
}
