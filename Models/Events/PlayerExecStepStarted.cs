using MediatR;

namespace Toko.Models.Events
{
    public class PlayerExecStepStarted : IRoomEvent
    {
        public string RoomId { get; }
        public int Round { get; }
        public int Step { get; }
        public string PlayerId { get; }

        public PlayerExecStepStarted(string roomId, int round, int step, string playerId)
        {
            RoomId = roomId;
            Round = round;
            Step = step;
            PlayerId = playerId;
        }
    }
    
}