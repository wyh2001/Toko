using MediatR;
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
    public record PlayerAutoMoved(string RoomId, int Round, int Step, string PlayerId, string PlayerName, int MoveDistance) : IRoomEvent, ILogEvent
    {
        public string ToLogMessage() => $"Step ends, {PlayerName} moved forward)";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerTimeoutElapsed(string RoomId, string PlayerId, string PlayerName) : IRoomEvent;
    public record RoomEnded(string RoomId, GameEndReason Reason, List<PlayerResult> Results) : IRoomEvent;
    public record RoomSettingsUpdated(string RoomId, RoomSettings Settings) : IRoomEvent;
    public record RoomStarted(string RoomId, List<string> Order) : IRoomEvent;
    public record RoundAdvanced(string RoomId, int Round) : IRoomEvent;
    public record StepAdvanced(string RoomId, int Round, int Step, string? NextPhase = null) : IRoomEvent;
    public record LogUpdated(string RoomId, TurnLog Log) : IRoomEvent;

    // Log events
    public record PlayerMoved(string RoomId, int Round, int Step, string PlayerId, string PlayerName, int MoveDistance, int FromSegmentIndex, int FromLaneIndex, int FromCellIndex, int ToSegmentIndex, int ToLaneIndex, int ToCellIndex) : ILogEvent, INotification
    {
        public string ToLogMessage() => $"{PlayerName} moved forward {MoveDistance} space{(MoveDistance > 1 ? "s" : "")}";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerCollision(string RoomId, int Round, int Step, string PlayerId, string PlayerName, List<string> CollidedPlayerIds, List<string> CollidedPlayerNames, int SegmentIndex, int LaneIndex, int CellIndex) : ILogEvent, INotification
    {
        public string ToLogMessage() => CollidedPlayerIds.Count == 1 
            ? $"{PlayerName} collided with {CollidedPlayerNames[0]}"
            : $"{PlayerName} collided with {string.Join(", ", CollidedPlayerNames)}";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerChangedLane(string RoomId, int Round, int Step, string PlayerId, string PlayerName, int Direction, int FromLane, int ToLane, bool Success) : ILogEvent, INotification
    {
        public string ToLogMessage() => Success 
            ? $"{PlayerName} successfully changed lanes {(Direction > 0 ? "right" : "left")} from lane {FromLane} to lane {ToLane}"
            : $"{PlayerName} failed to change lanes {(Direction > 0 ? "right" : "left")} from lane {FromLane}";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerHitWall(string RoomId, int Round, int Step, string PlayerId, string PlayerName, int Direction, int AtLane) : ILogEvent, INotification
    {
        public string ToLogMessage() => $"{PlayerName} hit the wall trying to change lanes {(Direction > 0 ? "right" : "left")} from lane {AtLane}";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerLaneChangeBlocked(string RoomId, int Round, int Step, string PlayerId, string PlayerName, int Direction, List<string> BlockingPlayerIds, List<string> BlockingPlayerNames) : ILogEvent, INotification
    {
        public string ToLogMessage() => BlockingPlayerIds.Count == 1
            ? $"{PlayerName} couldn't change lanes {(Direction > 0 ? "right" : "left")} due to collision with {BlockingPlayerNames[0]}"
            : $"{PlayerName} couldn't change lanes {(Direction > 0 ? "right" : "left")} due to collision with {string.Join(", ", BlockingPlayerNames)}";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerChangedGear(string RoomId, int Round, int Step, string PlayerId, string PlayerName, int Direction, int FromGear, int ToGear) : ILogEvent, INotification
    {
        public string ToLogMessage() => Direction > 0 
            ? $"{PlayerName} shifted up from gear {FromGear} to gear {ToGear}"
            : $"{PlayerName} shifted down from gear {FromGear} to gear {ToGear}";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
    public record PlayerCornerLaneChangeFailed(string RoomId, int Round, int Step, string PlayerId, string PlayerName) : ILogEvent, INotification
    {
        public string ToLogMessage() => $"{PlayerName} tried to change lanes in a corner and received junk";
        public (int Round, int Step) GetRoundStep() => (Round, Step);
    }
}