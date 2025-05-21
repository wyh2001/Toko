namespace Toko.Models
{
    internal class PlayerDrawToSkip
    {
        private string id;
        private int currentRound;
        private int currentStep;
        private string pid;

        public PlayerDrawToSkip(string id, int currentRound, int currentStep, string pid)
        {
            this.id = id;
            this.currentRound = currentRound;
            this.currentStep = currentStep;
            this.pid = pid;
        }
    }
}