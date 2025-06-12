namespace Toko.Models.Events
{
    public class RoomStarted(string id, List<string> order)
    {
        public string Id { get; } = id;
        public List<string> Order { get; } = order;
    }
}