namespace Toko.Models.Events
{
    /// <summary>  
    /// Event triggered when a player is kicked from the room.  
    /// </summary>  
    public record PlayerKicked(string RoomId, string PlayerId) : IRoomEvent;
}
