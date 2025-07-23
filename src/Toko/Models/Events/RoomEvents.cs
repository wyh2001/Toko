using Toko.Shared.Models;
namespace Toko.Models.Events
{
    public interface ILogEvent
    {
        string RoomId { get; }
        string PlayerId { get; }
        string ToLogMessage();
        (int Round, int Step) GetRoundStep();
    }

    public record HostChanged(string RoomId, string NewHostId, string NewHostName) : IRoomEvent;
    public record PhaseChanged(string RoomId, Phase Phase, int Round, int Step) : IRoomEvent;
    public record PlayerBankUpdated(string RoomId, string PlayerId, string PlayerName, TimeSpan BankTime) : IRoomEvent;
    public record PlayerCardsDrawn(string RoomId, int Round, int Step, string PlayerId, string PlayerName, int HandCount) : IRoomEvent, ILogEvent
    {
        public string ToLogMessage() => $"{PlayerName} drew {HandCount} card{(HandCount > 1 ? "s" : "")}";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerCardSubmissionStarted(string RoomId, int Round, int Step, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerCardSubmitted(string RoomId, int Round, int Step, string PlayerId, string PlayerName, string CardId, CardType CardType) : IRoomEvent, ILogEvent
    {
        public string ToLogMessage() => $"{PlayerName} submitted {CardType} card";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerDiscardExecuted(string RoomId, int Round, int Step, string PlayerId, string PlayerName, List<string> DiscardedCardIds) : IRoomEvent, ILogEvent
    {
        public string ToLogMessage() => DiscardedCardIds.Count == 0
            ? $"{PlayerName} chose not to discard any cards"
            : $"{PlayerName} discarded {DiscardedCardIds.Count} card{(DiscardedCardIds.Count > 1 ? "s" : "")}";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerDiscardStarted(string RoomId, int Round, int Step, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerDrawToSkip(string RoomId, int Round, int Step, string PlayerId, string PlayerName) : IRoomEvent, ILogEvent
    {
        public string ToLogMessage() => $"{PlayerName} chose to skip and draw cards";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerExecStepStarted(string RoomId, int Round, int Step, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerFinished(string RoomId, int Round, int Step, string PlayerId, string PlayerName) : IRoomEvent, ILogEvent
    {
        public string ToLogMessage() => $"{PlayerName} crossed the finish line!";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerJoined(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerKicked(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerLeft(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerParameterSubmissionSkipped(string RoomId, int Round, int Step, string PlayerId, string PlayerName) : IRoomEvent;
    public record PlayerParameterSubmissionStarted(string RoomId, int Round, int Step, string PlayerId, string PlayerName, CardType CardType) : IRoomEvent;
    public record PlayerReadyToggled(string RoomId, string PlayerId, string PlayerName, bool IsReady) : IRoomEvent;
    public record PlayerStepExecuted(string RoomId, int Round, int Step) : IRoomEvent;
    public record PlayerAutoMoved(string RoomId, int Round, int Step, string PlayerId, string PlayerName, int MoveDistance, int NewSegmentIndex, int NewLaneIndex, int NewCellIndex) : IRoomEvent, ILogEvent
    {
        public string ToLogMessage() => $"Player automatically moved {MoveDistance} spaces to position ({NewSegmentIndex},{NewLaneIndex},{NewCellIndex})";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerTimeoutElapsed(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
    public record RoomEnded(string RoomId, GameEndReason Reason, List<PlayerResult> Results) : IRoomEvent;
    public record RoomSettingsUpdated(string RoomId, RoomSettings Settings) : IRoomEvent;
    public record RoomStarted(string RoomId, List<string> Order) : IRoomEvent;
    public record RoundAdvanced(string RoomId, int Round) : IRoomEvent;
    public record StepAdvanced(string RoomId, int Round, int Step, string? NextPhase = null) : IRoomEvent;
    public record LogUpdated(string RoomId, TurnLog Log) : IRoomEvent;
}