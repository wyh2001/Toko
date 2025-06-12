using MediatR;

namespace Toko.Models.Events
{
    public class StepAdvanced : IRoomEvent
    {
        public string RoomId { get; }
        public int Round { get; }
        public int Step { get; }

        public StepAdvanced(string roomId, int round, int step)
        {
            RoomId = roomId;
            Round = round;
            Step = step;
        }
    }
}
