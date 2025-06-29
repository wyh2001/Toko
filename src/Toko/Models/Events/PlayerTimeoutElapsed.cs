namespace Toko.Models.Events
{
    /// <summary>  
    /// Event triggered when a player's timeout has elapsed.  
    /// </summary>  
    public record PlayerTimeoutElapsed(string RoomId, string PlayerId) : IRoomEvent;
}
