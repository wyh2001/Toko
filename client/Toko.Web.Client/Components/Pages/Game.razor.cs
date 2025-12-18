using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;
using Toko.Shared.Models;
using Toko.Web.Client.Services;

namespace Toko.Web.Client.Components.Pages;

public partial class Game : ComponentBase, IAsyncDisposable
{
    [Parameter] public string RoomId { get; set; } = string.Empty;

    [Inject] protected GameStateService GameStateService { get; set; } = default!;
    [Inject] protected IAuthenticationService AuthService { get; set; } = default!;

    // Properties needed by Razor template
    private string roomName => GameStateService.RoomName;
    private bool isConnected => GameStateService.IsConnected;
    private bool isReconnecting => GameStateService.IsReconnecting;
    private bool isLoading => GameStateService.IsLoading;
    private RoomStatusSnapshot? gameData => GameStateService.GameData;
    private int DisplayedRound => GameStateService.DisplayedRound;
    private bool gameEnded => GameStateService.GameEnded;
    private bool showGameEndedOverlay => GameStateService.ShowGameEndedOverlay;
    private List<CardDto> playerHand => GameStateService.PlayerHand.ToList();
    private List<CardDto> junkCards => GameStateService.JunkCards.ToList();
    private List<CardDto> discardableCards => GameStateService.DiscardableCards.ToList();
    private string selectedCard => GameStateService.SelectedCard;
    private List<string> selectedJunkCards => GameStateService.SelectedJunkCards.ToList();
    private List<string> selectedDiscardCards => GameStateService.SelectedDiscardCards.ToList();
    private int changeLaneEffect => GameStateService.ChangeLaneEffect;
    private int shiftGearEffect => GameStateService.ShiftGearEffect;
    private bool isMyTurn => GameStateService.IsMyTurn;
    private Phase gamePhase => GameStateService.GamePhase;
    private string currentTurnPlayer => GameStateService.CurrentTurnPlayer ?? "";
    private int discardCountdown => GameStateService.DiscardCountdown;
    private List<TurnLog> gameLogs => GameStateService.GameLogs.ToList();
    private string gamePhaseString => GameStateService.GamePhaseString;

    protected override async Task OnInitializedAsync()
    {
        await GameStateService.InitializeAsync(RoomId);
        GameStateService.OnChange += StateHasChanged;
    }

    // Simple delegation methods for UI events
    private void HandleCardClick(CardDto card) => GameStateService.HandleCardClick(card);
    private async Task PlaySelectedCard() => await GameStateService.PlaySelectedCard();
    private async Task DrawCards() => await GameStateService.DrawCards();
    private void SetChangeLaneEffect(int effect) => GameStateService.SetChangeLaneEffect(effect);
    private void SetShiftGearEffect(int effect) => GameStateService.SetShiftGearEffect(effect);
    private async Task SubmitParameters() => await GameStateService.SubmitParameters();
    private async Task SubmitDiscardCards() => await GameStateService.SubmitDiscardCards();
    private async Task SkipDiscard() => await GameStateService.SkipDiscard();
    private async Task LeaveGame() => await GameStateService.LeaveGame();
    private void ShowGameMenu() => GameStateService.ShowGameMenu();
    private void GoHome() => GameStateService.GoHome();
    private void ViewRoom() => GameStateService.ViewRoom();
    private void ReviewGame() => GameStateService.ReviewGame();

    // Helper methods that need to remain for UI calculations
    private string GetCardSelectionClass(CardDto card) => GameStateService.GetCardSelectionClass(card);
    private bool IsCardPlayable(CardDto card) => GameStateService.IsCardPlayable(card);
    private bool IsParameterSubmissionReady() => GameStateService.IsParameterSubmissionReady();
    private bool IsPlayerInDiscardPhase() => GameStateService.IsPlayerInDiscardPhase();

    // UI-specific helper methods
    private string GetPlayerName(string? playerId) => GameStateService.GetPlayerName(playerId);

    private string GetPlayerInitials(string playerName)
    {
        var parts = playerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpper() : playerName[..Math.Min(2, playerName.Length)].ToUpper();
    }

    private string GetPlayerColorClass(string playerId)
    {
        var index = GameStateService.GameData?.Racers?.FindIndex(r => r.Id == playerId) ?? 0;
        var colors = new[] { "color-red", "color-blue", "color-green", "color-yellow" };
        return colors[index % colors.Length];
    }

    private string GetCardValue(string cardType) => cardType switch
    {
        "ChangeLane" => "L",
        "ShiftGear" => "G",
        "Junk" => "J",
        "Repair" => "R",
        _ => "?"
    };

    // Lane change direction helpers (needed for UI)
    private (List<(string Text, int Value)>, string LayoutClass) GetLaneChangeButtonsAndLayout() =>
        GameStateService.GetLaneChangeButtonsAndLayout();

    // Card overlay helper methods
    private bool ShouldShowCardDisabledOverlay(CardDto card) => GameStateService.ShouldShowCardDisabledOverlay(card);
    private string GetCardDisabledMessage(CardDto card) => GameStateService.GetCardDisabledMessage(card);

    public async ValueTask DisposeAsync()
    {
        if (GameStateService != null)
        {
            GameStateService.OnChange -= StateHasChanged;
            await GameStateService.DisposeAsync();
        }
    }

    private string GetPlayerTimeBankDisplay(string playerId) => GameStateService.GetPlayerTimeBankDisplay(playerId);
    private string GetPlayerNameSafe(string? playerId) => GameStateService.GetPlayerNameSafe(playerId);
    private int GetPlayerPosition(string playerId) => GameStateService.GetPlayerPosition(playerId);
    private int GetPlayerGear(string playerId) => GameStateService.GetPlayerGear(playerId);
    private bool HasJunkCardsSelected() => GameStateService.HasJunkCardsSelected();
}
