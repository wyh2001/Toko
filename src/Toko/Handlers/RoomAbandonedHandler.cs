using MediatR;
using Toko.Models.Events;
using Toko.Services;

namespace Toko.Handlers
{
    public class RoomAbandonedHandler(RoomManager rm) : INotificationHandler<RoomAbandoned>
    {
        private readonly RoomManager _rm = rm;

        public Task Handle(RoomAbandoned e, CancellationToken ct)
            => _rm.FinalizeAbandonRoom(e.RoomId)
                  ? Task.CompletedTask
                  : throw new InvalidOperationException("Room not found");
    }
}
