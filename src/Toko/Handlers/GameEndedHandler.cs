using MediatR;
using Toko.Models.Events;
using Toko.Services;

namespace Toko.Handlers
{
    public class GameEndedHandler(RoomManager rm) : INotificationHandler<GameEnded>
    {
        private readonly RoomManager _rm = rm;

        public Task Handle(GameEnded e, CancellationToken ct)
            => _rm.FinalizeEndGame(e.RoomId, e.Reason)
                  ? Task.CompletedTask
                  : throw new InvalidOperationException("Room not found");
    }
}
