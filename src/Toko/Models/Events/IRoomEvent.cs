
namespace Toko.Models.Events
{
    public interface IRoomEvent : IEvent
    {
        string RoomId { get; }
    }
}
