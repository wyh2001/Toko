using MediatR;
using Microsoft.AspNetCore.SignalR;
using Toko.Hubs;
using Toko.Models.Events;

namespace Toko.Services
{
    public class PlayerSubmissionStepStartedHandler : INotificationHandler<PlayerSubmissionStepStarted>
    {
        private readonly IHubContext<RaceHub> _hub;
        public PlayerSubmissionStepStartedHandler(IHubContext<RaceHub> hub) => _hub = hub;

        public async Task Handle(PlayerSubmissionStepStarted evt, CancellationToken ct)
        {
            await _hub.Clients
                      .Group(evt.RoomId)
                      .SendAsync("AskPlayerSubmit", evt.Round, evt.Step, evt.PlayerId, ct);
        }
    }
}
