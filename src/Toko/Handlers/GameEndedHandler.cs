using Toko.Shared.Models.Events;
using Toko.Services;
using Toko.Infrastructure.Eventing;

namespace Toko.Handlers
{
    public class GameEndedHandler(RoomManager rm, ILogger<GameEndedHandler> logger) : IChannelEventHandler
    {
        private readonly RoomManager _rm = rm;
        private readonly ILogger<GameEndedHandler> _logger = logger;

        public Task HandleAsync(IEvent ev, CancellationToken ct)
        {
            var e = (GameEnded)ev;
            if (!_rm.FinalizeEndGame(e.RoomId, e.Reason))
                _logger.LogWarning("Failed to finalize end game for room {RoomId}", e.RoomId);
            return Task.CompletedTask;
        }
    }
}
