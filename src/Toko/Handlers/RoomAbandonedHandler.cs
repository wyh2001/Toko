using Toko.Models.Events;
using Toko.Services;
using Toko.Infrastructure.Eventing;

namespace Toko.Handlers
{
    public class RoomAbandonedHandler(RoomManager rm) : IChannelEventHandler
    {
        private readonly RoomManager _rm = rm;

        public Task HandleAsync(IEvent ev, CancellationToken ct)
        {
            var e = (RoomAbandoned)ev;
            return _rm.FinalizeAbandonRoom(e.RoomId)
                ? Task.CompletedTask
                : throw new InvalidOperationException("Room not found");
        }
    }
}
