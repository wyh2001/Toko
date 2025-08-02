using Microsoft.AspNetCore.SignalR;
using Toko.Hubs;
using Toko.Models.Events;
using Toko.Infrastructure.Eventing;
using Toko.Shared.Models.Events;

namespace Toko.Handlers
{
    public class RoomEventHandler(IHubContext<RaceHub> hub) : IChannelEventHandler
    {
        private readonly IHubContext<RaceHub> _hub = hub;

        public Task HandleAsync(IEvent ev, CancellationToken ct)
        {
            var re = (IRoomEvent)ev;
            var eventName = ev.GetType().Name;
            return _hub.Clients.Group(re.RoomId)
                               .SendAsync("OnRoomEvent", eventName, re, ct);
        }
    }
}
