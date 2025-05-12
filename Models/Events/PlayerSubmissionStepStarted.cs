using MediatR;

namespace Toko.Models.Events
{
    public class PlayerSubmissionStepStarted : INotification
    {
        public string RoomId { get; }
        public int Round { get; }
        public int Step { get; }
        public string PlayerId { get; }

        public PlayerSubmissionStepStarted(string roomId, int round, int step, string playerId)
        {
            RoomId = roomId;
            Round = round;
            Step = step;
            PlayerId = playerId;
        }
    }
}
