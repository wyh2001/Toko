using System.Threading.Channels;
using Toko.Models.Events;
using Toko.Shared.Models.Events;

namespace Toko.Infrastructure.Eventing;

public class DefaultEventChannel : IEventChannel
{
    private readonly Channel<IEvent> _channel =
        Channel.CreateUnbounded<IEvent>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = false,
            AllowSynchronousContinuations = false
        });

    public ValueTask PublishAsync<T>(T evt, CancellationToken ct = default)
        where T : class, IEvent
        => _channel.Writer.WriteAsync(evt, ct);

    public IAsyncEnumerable<IEvent> ReadAllAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct);
}
