using Toko.Shared.Models.Events;
using Toko.Services;
using Toko.Infrastructure.Eventing;

namespace Toko.Handlers
{
    public class GameEndedHandler(RoomManager rm) : IChannelEventHandler
    {
        private readonly RoomManager _rm = rm;

        public Task HandleAsync(IEvent ev, CancellationToken ct)
        {
            var e = (GameEnded)ev;
            return _rm.FinalizeEndGame(e.RoomId, e.Reason)
                ? Task.CompletedTask
                : throw new InvalidOperationException("Room not found");
        }
    }
}
