
using MediatR;
using Toko.Models;
using Toko.Models.Events;
using Toko.Services;
using Toko.Shared.Models;

namespace Toko.Handlers;
public class LogEventHandler<TEvent>(IMediator mediator, RoomManager roomManager)
        : INotificationHandler<TEvent> where TEvent : class, ILogEvent, INotification
{
    private readonly IMediator _mediator = mediator;
    private readonly RoomManager _roomManager = roomManager;

    public async Task Handle(TEvent e, CancellationToken ct)
    {
        var logMessage = e.ToLogMessage();
        var (round, step) = e.GetRoundStep();
        var log = new TurnLog(logMessage, e.PlayerId, round, step);

        _roomManager.AddLogToRoom(e.RoomId, log);

        await _mediator.Publish(new LogUpdated(e.RoomId, log), ct);
    }
}