namespace Toko.Models.Events
{
    public record RoomStarted(string RoomId, List<string> Order) : IRoomEvent;
}