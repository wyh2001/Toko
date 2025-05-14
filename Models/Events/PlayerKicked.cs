namespace Toko.Models.Events
{
    /// <summary>  
    /// Event triggered when a player is kicked from the room.  
    /// </summary>  
    public class PlayerKicked
    {
        public string RoomId { get; }
        public string PlayerId { get; }

        public PlayerKicked(string roomId, string playerId)
        {
            RoomId = roomId;
            PlayerId = playerId;
        }
    }
}
