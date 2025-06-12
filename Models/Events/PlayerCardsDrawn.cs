using MediatR;

namespace Toko.Models.Events
{
    public class PlayerCardsDrawn(string roomId, int round, int step, string playerId, int handCount) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public int Round { get; } = round;
        public int Step { get; } = step;
        public string PlayerId { get; } = playerId;
        public int HandCount { get; } = handCount;
    }
}
