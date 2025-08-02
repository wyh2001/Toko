using Toko.Infrastructure.Eventing;
using Toko.Models.Events;
using Toko.Handlers;
using Toko.Shared.Models.Events;

namespace Toko.Infrastructure.Eventing;

public class ChannelDispatchService(IEventChannel channel, IServiceProvider sp, ILogger<ChannelDispatchService> logger) : BackgroundService
{
    private readonly IEventChannel _channel = channel;
    private readonly IServiceProvider _sp = sp;
    private readonly ILogger<ChannelDispatchService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in _channel.ReadAllAsync(stoppingToken))
        {
            var task = evt switch
            {
                GameEnded e => HandleAsync<GameEndedHandler>(e, stoppingToken),
                RoomAbandoned e => HandleAsync<RoomAbandonedHandler>(e, stoppingToken),
                ILogEvent le => HandleAsync<LogEventHandler>((IEvent)le, stoppingToken),
                IRoomEvent re => HandleAsync<RoomEventHandler>((IEvent)re, stoppingToken),
                _ => Task.CompletedTask
            };
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    var roomId = evt switch
                    {
                        IRoomEvent re => re.RoomId,
                        ILogEvent le => le.RoomId,
                        _ => "Unknown"
                    };
                    _logger.LogError(t.Exception, "Handler failed for event {EventType} in room {RoomId}", evt.GetType().Name, roomId);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private Task HandleAsync<THandler>(IEvent evt, CancellationToken ct) where THandler : class
    {
        if (_sp.GetService(typeof(THandler)) is not THandler handler)
            return Task.CompletedTask;

        return handler switch
        {
            IChannelEventHandler ch => ch.HandleAsync(evt, ct),
            _ => Task.CompletedTask
        };
    }
}
