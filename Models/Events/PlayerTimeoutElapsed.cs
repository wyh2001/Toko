namespace Toko.Models.Events
{
    /// <summary>  
    /// Event triggered when a player's timeout has elapsed.  
    /// </summary>  
    public class PlayerTimeoutElapsed
    {
        public string RoomId { get; }
        public string PlayerId { get; }

        public PlayerTimeoutElapsed(string roomId, string playerId)
        {
            RoomId = roomId;
            PlayerId = playerId;
        }
    }
}
