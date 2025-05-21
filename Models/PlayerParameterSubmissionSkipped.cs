namespace Toko.Models
{
    internal class PlayerParameterSubmissionSkipped
    {
        private string id;
        private int currentRound;
        private int currentStep;
        private string playerId;

        public PlayerParameterSubmissionSkipped(string id, int currentRound, int currentStep, string playerId)
        {
            this.id = id;
            this.currentRound = currentRound;
            this.currentStep = currentStep;
            this.playerId = playerId;
        }
    }
}