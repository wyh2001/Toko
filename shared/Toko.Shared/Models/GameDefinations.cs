namespace Toko.Shared.Models
{
    public record PlayerResult(string PlayerId, int Rank, int Score);
    public enum Phase { CollectingCards, CollectingParams, Discarding }
    public enum CardType
    {
        ChangeLane,
        Junk,
        Repair,
        ShiftGear
    }
    public enum GameEndReason
    {
        FinisherCrossedLine,
        NoActivePlayersLeft,
        TurnLimitReached,
    }
    public record RoomSettings(string? Name, int? MaxPlayers, bool? IsPrivate, List<int>? StepsPerRound); // at this time, no map for simplicity
}
