namespace Toko.Models.Events
{
    /// <summary>  
    /// Event triggered when a player starts the discard phase.  
    /// </summary>  
    public record PlayerDiscardStarted(string RoomId, int Round, int Step, string PlayerId) : IRoomEvent;
}
