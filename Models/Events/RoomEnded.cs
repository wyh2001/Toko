namespace Toko.Models.Events
{
    public class RoomEnded(string roomId)
    {
        public string RoomId { get; } = roomId;
    }
}
