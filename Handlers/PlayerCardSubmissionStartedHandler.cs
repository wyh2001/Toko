using MediatR;
using Microsoft.AspNetCore.SignalR;
using Toko.Hubs;
using Toko.Models.Events;

namespace Toko.Handlers
{
    public class PlayerCardSubmissionStartedHandler : INotificationHandler<PlayerCardSubmissionStarted>
    {
        private readonly IHubContext<RaceHub> _hub;
        public PlayerCardSubmissionStartedHandler(IHubContext<RaceHub> hub) => _hub = hub;

        public async Task Handle(PlayerCardSubmissionStarted evt, CancellationToken ct)
        {
            await _hub.Clients
                      .Group(evt.RoomId)
                      .SendAsync("AskPlayerSubmitCard", evt.Round, evt.Step, evt.PlayerId, ct);
        }
    }
}
