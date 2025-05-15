namespace Toko.Models.Events
{
    /// <summary>  
    /// Event triggered when a player starts the discard phase.  
    /// </summary>  
    public class PlayerDiscardStarted : IRoomEvent
    {
        public string RoomId { get; }
        public int Round { get; }
        public int Step { get; }
        public string PlayerId { get; }

        public PlayerDiscardStarted(string roomId, int round, int step, string playerId)
        {
            RoomId = roomId;
            Round = round;
            Step = step;
            PlayerId = playerId;
        }
    }
}
