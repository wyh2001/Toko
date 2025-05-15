namespace Toko.Models.Events
{
    public class PlayerDiscardExecuted : IRoomEvent
    {
        public string RoomId { get; }
        public int CurrentRound { get; }
        public int CurrentStep { get; }
        public string PlayerId { get; }
        public List<string> DiscardedCardIds { get; }

        public PlayerDiscardExecuted(string roomId, int currentRound, int currentStep, string playerId, List<string> discardedCardIds)
        {
            RoomId = roomId;
            CurrentRound = currentRound;
            CurrentStep = currentStep;
            PlayerId = playerId;
            DiscardedCardIds = discardedCardIds;
        }
    }
}
