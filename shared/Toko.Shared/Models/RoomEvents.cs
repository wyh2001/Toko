namespace Toko.Shared.Models
{
    public record HostChanged(string RoomId, string NewHostId, string NewHostName);
    public record PhaseChanged(string RoomId, Phase Phase, int Round, int Step);
    public record PlayerBankUpdated(string RoomId, string PlayerId, TimeSpan BankTime);
    public record PlayerCardsDrawn(string RoomId, int Round, int Step, string PlayerId, int HandCount);
    public record PlayerCardSubmissionStarted(string RoomId, int Round, int Step, string PlayerId);
    public record PlayerCardSubmitted(string RoomId, int Round, int Step, string PlayerId, string CardId);
    public record PlayerDiscardExecuted(string RoomId, int Round, int Step, string PlayerId, List<string> DiscardedCardIds);
    public record PlayerDiscardStarted(string RoomId, int Round, int Step, string PlayerId);
    public record PlayerDrawToSkip(string RoomId, int CurrentRound, int CurrentStep, string PlayerId);
    public record PlayerExecStepStarted(string RoomId, int Round, int Step, string PlayerId);
    public record PlayerFinished(string RoomId, string PlayerId);
    public record PlayerJoined(string RoomId, string PlayerId, string PlayerName);
    public record PlayerKicked(string RoomId, string PlayerId);
    public record PlayerLeft(string RoomId, string PlayerId, string PlayerName);
    public record PlayerParameterSubmissionSkipped(string RoomId, int CurrentRound, int CurrentStep, string PlayerId);
    public record PlayerParameterSubmissionStarted(string RoomId, int Round, int Step, string PlayerId, CardType CardType);
    public record PlayerReadyToggled(string RoomId, string PlayerId, bool IsReady);
    public record PlayerStepExecuted(string RoomId, int Round, int Step);
    public record PlayerTimeoutElapsed(string RoomId, string PlayerId);
    public record RoomEnded(string RoomId, GameEndReason Reason, List<PlayerResult> Results);
    public record RoomSettingsUpdated(string RoomId, RoomSettings Settings);
    public record RoomStarted(string RoomId, List<string> Order);
    public record RoundAdvanced(string RoomId, int Round);
    public record StepAdvanced(string RoomId, int Round, int Step, string? NextPhase = null);
}