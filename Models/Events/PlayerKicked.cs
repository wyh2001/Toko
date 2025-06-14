namespace Toko.Models.Events
{
    /// <summary>  
    /// Event triggered when a player is kicked from the room.  
    /// </summary>  
    public class PlayerKicked(string roomId, string playerId) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public string PlayerId { get; } = playerId;
        //public int CurrentRound { get; } = currentRound;
        //public int CurrentStep { get; } = currentStep;
    }
}
