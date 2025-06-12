using MediatR;

namespace Toko.Models.Events
{
    public class PlayerCardSubmitted(string roomId, int round, int step, string playerId, string cardId) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public int Round { get; } = round;
        public int Step { get; } = step;
        public string PlayerId { get; } = playerId;
        public string CardId { get; } = cardId;
    }
}
