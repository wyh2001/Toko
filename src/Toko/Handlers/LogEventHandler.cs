using Toko.Models;
using Toko.Models.Events;
using Toko.Services;
using Toko.Shared.Models;
using Toko.Infrastructure.Eventing;

namespace Toko.Handlers;
public class LogEventHandler(IEventChannel events, RoomManager roomManager) : IChannelEventHandler
{
    private readonly IEventChannel _events = events;
    private readonly RoomManager _roomManager = roomManager;

    public async Task HandleAsync(IEvent ev, CancellationToken ct)
    {
        var e = (ILogEvent)ev;
        var logMessage = e.ToLogMessage();
        var (round, step) = e.GetRoundStep();
        var log = new TurnLog(logMessage, e.PlayerId, round, step);

        _roomManager.AddLogToRoom(e.RoomId, log);

        await _events.PublishAsync(new LogUpdated(e.RoomId, log), ct);
    }
}