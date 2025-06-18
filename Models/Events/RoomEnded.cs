using static Toko.Models.Room;

namespace Toko.Models.Events
{
    public class RoomEnded(string roomId, GameEndReason reason, List<PlayerResult> results) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public GameEndReason Reason { get; } = reason;
        public List<PlayerResult> Results { get; } = results;
    }
}
