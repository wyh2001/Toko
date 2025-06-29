using MediatR;

namespace Toko.Models.Events
{
    public interface IRoomEvent : INotification
    {
        string RoomId { get; }
    }
}
