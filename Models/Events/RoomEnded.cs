namespace Toko.Models.Events
{
    public class RoomEnded(string roomId) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
    }
}
