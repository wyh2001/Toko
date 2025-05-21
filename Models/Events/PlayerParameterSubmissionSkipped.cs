namespace Toko.Models.Events
{
    internal class PlayerParameterSubmissionSkipped : IRoomEvent
    {
        public string RoomId { get; }
        public int currentRound;
        public int currentStep;
        public string playerId;

        public PlayerParameterSubmissionSkipped(string id, int currentRound, int currentStep, string playerId)
        {
            this.RoomId = id;
            this.currentRound = currentRound;
            this.currentStep = currentStep;
            this.playerId = playerId;
        }
    }
}