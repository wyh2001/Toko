namespace Toko.Shared.Models
{
    public record ApiSuccess<T>(string Message, T? Data);
    // Response DTOs
    public record CreateRoomDto(string RoomId, string PlayerId, string PlayerName);
    public record JoinRoomDto(string RoomId, string PlayerId, string PlayerName);
    public record AuthDto(string PlayerName, string PlayerId);
    public record WaitingCountDto(long Count);
    public record CompletedCountDto(long Count);
    public record RoomCountsDto(long WaitingCount, long PlayingCount, long PlayingRacersCount, long FinishedCount);
    public record RoomStatusSnapshot(
    string RoomId,
    string Name,
    int MaxPlayers,
    bool IsPrivate,
    string Status,
    string Phase,
    int CurrentRound,
    int CurrentStep,
    int TotalRounds,
    int TotalSteps,
    string? CurrentTurnPlayerId,
    string? CurrentTurnCardType, // Add card type for parameter submission
    List<string> DiscardPendingPlayerIds,
    List<RacerStatus> Racers,
    MapSnapshot Map,
    List<PlayerResult>? Results
        );
    public record RacerStatus(string Id, string Name, int Segment, int Lane, int Tile, double Bank, bool IsHost, bool IsReady, int HandCount, bool IsBanned);
    public record RoomListItemDto(string Id, string Name, int MaxPlayers, bool IsPrivate, List<RacerStatus> Racers, string Status);
    public record DrawSkipDto(string RoomId, string PlayerId, List<CardDto> DrawnCards);
    public record CardDto(string Id, string Type);
    public record SubmitStepCardDto(string RoomId, string PlayerId, string CardId);
    public record SubmitExecParamDto(string RoomId, string PlayerId, object Instruction);
    public record LeaveRoomDto(string RoomId, string PlayerId);
    public record DiscardCardsDto(string RoomId, string PlayerId, List<string> CardIds);
    public record ReadyUpDto(string RoomId, string PlayerId, bool IsReady);
    public record GetHandDto(string RoomId, string PlayerId, List<CardDto> Cards);
    public record KickPlayerDto(string RoomId, string PlayerId, string KickedPlayerId);
    public record UpdateRoomSettingsDto(string RoomId, string PlayerId, object Settings);
}
