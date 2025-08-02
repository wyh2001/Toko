using Toko.Models.Events;
using Toko.Services;
using Toko.Infrastructure.Eventing;
using Toko.Shared.Models.Events;

namespace Toko.Handlers
{
    public class RoomAbandonedHandler(RoomManager rm, ILogger<RoomAbandonedHandler> logger) : IChannelEventHandler
    {
        private readonly RoomManager _rm = rm;
        private readonly ILogger<RoomAbandonedHandler> _logger = logger;

        public Task HandleAsync(IEvent ev, CancellationToken ct)
        {
            var e = (RoomAbandoned)ev;
            if (!_rm.FinalizeAbandonRoom(e.RoomId))
                _logger.LogWarning("Failed to finalize abandon room for room {RoomId}", e.RoomId);
            return Task.CompletedTask;
        }
    }
}
