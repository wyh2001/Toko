using MediatR;
using Microsoft.AspNetCore.SignalR;
using Toko.Hubs;
using Toko.Models.Events;

namespace Toko.Services
{
    public class PlayerParameterSubmissionStartedHandler : INotificationHandler<PlayerParameterSubmissionStarted>
    {
        private readonly IHubContext<RaceHub> _hub;
        public PlayerParameterSubmissionStartedHandler(IHubContext<RaceHub> hub) => _hub = hub;

        public async Task Handle(PlayerParameterSubmissionStarted evt, CancellationToken ct)
        {
            await _hub.Clients
                      .Group(evt.RoomId)
                      .SendAsync("AskPlayerSubmitParameter", evt.Round, evt.Step, evt.PlayerId, ct);
        }
    }
}
