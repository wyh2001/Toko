using MediatR;
using Microsoft.AspNetCore.SignalR;
using Toko.Hubs;
using Toko.Models.Events;

public class RoomEventHandler<TEvent>(IHubContext<RaceHub> hub) : INotificationHandler<TEvent>
    where TEvent : IRoomEvent
{
    private readonly IHubContext<RaceHub> _hub = hub;

    public Task Handle(TEvent evt, CancellationToken ct)
    {
        // 编译期保证有 RoomId
        var roomId = evt.RoomId;
        var eventName = typeof(TEvent).Name;

        // 直接推给该房间的所有客户端
        return _hub.Clients
                   .Group(roomId)
                   .SendAsync("OnRoomEvent", eventName, evt, ct);
    }
}
