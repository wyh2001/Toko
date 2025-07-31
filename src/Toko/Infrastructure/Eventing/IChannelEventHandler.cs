using Toko.Models.Events;

namespace Toko.Infrastructure.Eventing;

public interface IChannelEventHandler
{
    Task HandleAsync(IEvent evt, CancellationToken ct);
}
