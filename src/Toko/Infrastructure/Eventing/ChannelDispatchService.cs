using Toko.Infrastructure.Eventing;
using Toko.Models.Events;
using Toko.Handlers;
using Toko.Shared.Models.Events;

namespace Toko.Infrastructure.Eventing;

public class ChannelDispatchService(IEventChannel channel, IServiceProvider sp) : BackgroundService
{
    private readonly IEventChannel _channel = channel;
    private readonly IServiceProvider _sp = sp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in _channel.ReadAllAsync(stoppingToken))
        {
            _ = evt switch
            {
                GameEnded e => HandleAsync<GameEndedHandler>(e, stoppingToken),
                RoomAbandoned e => HandleAsync<RoomAbandonedHandler>(e, stoppingToken),
                ILogEvent le => HandleAsync<LogEventHandler>((IEvent)le, stoppingToken),
                IRoomEvent re => HandleAsync<RoomEventHandler>((IEvent)re, stoppingToken),
                _ => Task.CompletedTask
            };
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
