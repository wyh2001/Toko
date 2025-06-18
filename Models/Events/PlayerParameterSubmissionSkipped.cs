namespace Toko.Models.Events
{
    public record PlayerParameterSubmissionSkipped(string RoomId, int CurrentRound, int CurrentStep, string PlayerId) : IRoomEvent;
}