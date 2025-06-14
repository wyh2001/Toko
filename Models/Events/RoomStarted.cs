namespace Toko.Models.Events
{
    public class RoomStarted(string id, List<string> order) : IRoomEvent
    {
        public string RoomId { get; } = id;
        public List<string> Order { get; } = order;
    }
}