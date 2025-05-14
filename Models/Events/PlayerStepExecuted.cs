using MediatR;

namespace Toko.Models.Events
{
    public class PlayerStepExecuted : INotification
    {
        public string RoomId { get; }
        public int Round { get; }
        public int Step { get; }
        public List<TurnLog> Logs { get; }

        public PlayerStepExecuted(string roomId, int round, int step, List<TurnLog> logs)
        {
            RoomId = roomId;
            Round = round;
            Step = step;
            Logs = logs;
        }
    }
}
