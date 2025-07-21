using Toko.Services;
using Toko.Shared.Models;
using static Toko.Models.Room;
namespace Toko.Models.Events
{
    public record HostChanged(string RoomId, string NewHostId, string NewHostName) : IRoomEvent;
    public record PhaseChanged(string RoomId, Phase Phase, int Round, int Step) : IRoomEvent;
    public record PlayerBankUpdated(string RoomId, string PlayerId, TimeSpan BankTime) : IRoomEvent;
    public record PlayerCardsDrawn(string RoomId, int Round, int Step, string PlayerId, int HandCount) : IRoomEvent;
    public record PlayerCardSubmissionStarted(string RoomId, int Round, int Step, string PlayerId) : IRoomEvent;
    public record PlayerCardSubmitted(string RoomId, int Round, int Step, string PlayerId, string CardId) : IRoomEvent;
    public record PlayerDiscardExecuted(string RoomId, int Round, int Step, string PlayerId, List<string> DiscardedCardIds) : IRoomEvent;
    public record PlayerDiscardStarted(string RoomId, int Round, int Step, string PlayerId) : IRoomEvent;
    public record PlayerDrawToSkip(string RoomId, int CurrentRound, int CurrentStep, string PlayerId) : IRoomEvent;
    public record PlayerExecStepStarted(string RoomId, int Round, int Step, string PlayerId) : IRoomEvent;
    public record PlayerFinished(string RoomId, string PlayerId) : IRoomEvent;
    public record PlayerJoined(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerKicked(string RoomId, string PlayerId) : IRoomEvent;
    public record PlayerLeft(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerParameterSubmissionSkipped(string RoomId, int CurrentRound, int CurrentStep, string PlayerId) : IRoomEvent;
    public record PlayerParameterSubmissionStarted(string RoomId, int Round, int Step, string PlayerId, CardType CardType) : IRoomEvent;
    public record PlayerReadyToggled(string RoomId, string PlayerId, bool IsReady) : IRoomEvent;
    public record PlayerStepExecuted(string RoomId, int Round, int Step) : IRoomEvent;
    public record PlayerAutoMoved(string RoomId, int Round, int Step, string PlayerId, int MoveDistance, int NewSegmentIndex, int NewLaneIndex, int NewCellIndex) : IRoomEvent;
    public record PlayerTimeoutElapsed(string RoomId, string PlayerId) : IRoomEvent;
    public record RoomEnded(string RoomId, GameEndReason Reason, List<PlayerResult> Results) : IRoomEvent;
    public record RoomSettingsUpdated(string RoomId, RoomSettings Settings) : IRoomEvent;
    public record RoomStarted(string RoomId, List<string> Order) : IRoomEvent;
    public record RoundAdvanced(string RoomId, int Round) : IRoomEvent;
    public record StepAdvanced(string RoomId, int Round, int Step, string? NextPhase = null) : IRoomEvent;
}