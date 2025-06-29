namespace Toko.Models.Events
{
    public record PlayerDrawToSkip(string RoomId, int CurrentRound, int CurrentStep, string PlayerId) : IRoomEvent;
}