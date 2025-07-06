namespace Toko.Shared.Dtos
{
    public record ApiSuccess<T>(string Message, T? Data);
    // Response DTOs
    public record CreateRoomDto(string RoomId, string PlayerId, string PlayerName);
    public record JoinRoomDto(string RoomId, string PlayerId, string PlayerName);
    public record AuthDto(string PlayerName, string PlayerId);
    public record WaitingCountDto(long Count);
    public record CompletedCountDto(long Count);
    public record RoomCountsDto(long WaitingCount, long PlayingCount, long PlayingRacersCount, long FinishedCount);
    public record RacerDto(string Id, string Name, bool IsHost, bool IsReady);
    public record RoomDto(string Id, string? Name, int MaxPlayers, bool IsPrivate, List<RacerDto> Racers, string? Map, string Status);
    public record RoomListItemDto(string Id, string Name, int MaxPlayers, bool IsPrivate, List<RacerDto> Racers, string Status);
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
