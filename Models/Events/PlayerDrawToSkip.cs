namespace Toko.Models.Events
{
    public class PlayerDrawToSkip : IRoomEvent
    {
        public string RoomId { get; }
        public int CurrentRound { get; }
        public int CurrentStep { get; }
        public string PlayerId { get; }

        public PlayerDrawToSkip(string roomId, int currentRound, int currentStep, string playerId)
        {
            RoomId = roomId;
            CurrentRound = currentRound;
            CurrentStep = currentStep;
            PlayerId = playerId;
        }
    }
}