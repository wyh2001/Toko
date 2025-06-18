namespace Toko.Models.Events
{
    public record PlayerDiscardExecuted(string RoomId, int Round, int Step, string PlayerId, List<string> DiscardedCardIds) : IRoomEvent;
}
