namespace Toko.Shared.Models.Events
{
    public interface IRoomEvent : IEvent
    {
        string RoomId { get; }
    }
}