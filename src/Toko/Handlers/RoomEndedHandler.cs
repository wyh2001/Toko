using MediatR;
using Toko.Models.Events;
using Toko.Services;

namespace Toko.Handlers
{
    public class RoomEndedHandler(RoomManager rm) : INotificationHandler<RoomEnded>
    {
        private readonly RoomManager _rm = rm;

        public Task Handle(RoomEnded e, CancellationToken ct)
            => _rm.EndRoom(e.RoomId, e.Reason)
                  ? Task.CompletedTask
                  : throw new InvalidOperationException("Room not found");
    }
}
