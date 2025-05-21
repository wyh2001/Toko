namespace Toko.Models.Events
{
    public class PlayerDrawToSkip : IRoomEvent
    {
        public string RoomId { get; }
        public int currentRound;
        public int currentStep;
        public string pid;

        public PlayerDrawToSkip(string id, int currentRound, int currentStep, string pid)
        {
            RoomId = id;
            this.currentRound = currentRound;
            this.currentStep = currentStep;
            this.pid = pid;
        }
    }
}