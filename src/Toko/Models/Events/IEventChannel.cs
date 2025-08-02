using Toko.Shared.Models.Events;

namespace Toko.Models.Events;

public interface IEventChannel
{
    ValueTask PublishAsync<T>(T evt, CancellationToken ct = default) where T : class, IEvent;
    IAsyncEnumerable<IEvent> ReadAllAsync(CancellationToken ct = default);
}
