using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;
using Toko.Shared.Models;
using Toko.Web.Client.Services;

namespace Toko.Web.Client.Components.Pages;

public partial class Game : ComponentBase, IAsyncDisposable
{
    [Parameter] public string RoomId { get; set; } = string.Empty;

    [Inject] protected HttpClient Http { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected IAuthenticationService AuthService { get; set; } = default!;
    [Inject] protected IPlayerNameService PlayerNameService { get; set; } = default!;

    private HubConnection? hubConnection;
    
    private bool isLoading = true;
    private RoomStatusSnapshot? gameData;
    private string roomName = "";
    private Phase gamePhase = Phase.CollectingCards;
    private string gamePhaseString = "CollectingCards";
    private string? currentTurnPlayer;
    private bool isMyTurn = false;
    private bool gameEnded = false;
    private bool showGameEndedOverlay = false;
    private bool isConnected = true;
    private bool isReconnecting = false;
    
    private int DisplayedRound => gameData == null ? 0 : (gameEnded ? gameData.TotalRounds : gameData.CurrentRound + 1);
    
    private List<CardDto> playerHand = new();
    private string selectedCard = "";
    private List<TurnLog> gameLogs = new();
    private HashSet<Guid> seenLogIds = new(); // Track seen log UUIDs for deduplication
    
    // Parameter collection state
    private int changeLaneEffect = 1;
    private int shiftGearEffect = 1;
    private List<string> selectedJunkCards = new();
    private List<CardDto> junkCards = new();
    
    // Discarding phase state
    private List<string> selectedDiscardCards = new();
    private List<CardDto> discardableCards = new();

    // Time bank state
    private Dictionary<string, double> playerTimeBanks = new();
    private Dictionary<string, Timer?> playerTimers = new();
    private readonly object timerLock = new object();
    
    // Discard phase countdown timer
    private Timer? discardTimer;
    private int discardCountdown = 0;
    private readonly object discardTimerLock = new object();

    protected override async Task OnInitializedAsync()
    {
        await AuthService.EnsureAuthenticatedAsync();
        await InitializeSignalR();
        await LoadGameData();
        
        // Load hand data from API
        await LoadHandData();
        
    }

    private async Task LoadHandData()
    {
        try
        {
            // Only load hand data if game is playing
            if (gameData?.Status != "Playing") return;
            
            var response = await Http.GetFromJsonAsync<ApiSuccess<GetHandDto>>($"/api/room/{RoomId}/hand");
            
            if (response?.Data != null)
            {
                playerHand = response.Data.Cards;
                
                // Update junk cards for repair functionality
                junkCards = playerHand.Where(c => c.Type == "Junk").ToList();
                
                // Update discardable cards for discarding phase (exclude Junk cards)
                discardableCards = playerHand.Where(c => c.Type != "Junk").ToList();
                
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load hand data: {ex.Message}");
            // Don't clear the hand on error, just log it
        }
    }

    private async Task InitializeSignalR()
    {
        try
        {
            hubConnection = new HubConnectionBuilder()
                .WithUrl(Navigation.ToAbsoluteUri("/racehub"))
                .WithAutomaticReconnect()
                .Build();

            hubConnection.Closed += async (error) =>
            {
                isConnected = false;
                isReconnecting = false;
                await InvokeAsync(StateHasChanged);
                Console.WriteLine($"SignalR connection closed: {error?.Message}");
            };

            hubConnection.Reconnecting += async (error) =>
            {
                isConnected = false;
                isReconnecting = true;
                await InvokeAsync(StateHasChanged);
                Console.WriteLine($"SignalR reconnecting: {error?.Message}");
            };

            hubConnection.Reconnected += async (connectionId) =>
            {
                isConnected = true;
                isReconnecting = false;
                await InvokeAsync(StateHasChanged);
                Console.WriteLine($"SignalR reconnected with new connection ID: {connectionId}");
            };
                
            hubConnection.On<string, object>("OnRoomEvent", (eventName, eventData) =>
            {
                Console.WriteLine($"Game event received: {eventName}");
                
                switch (eventName)
                {
                    // Events that trigger a full state reload
                    case "HostChanged":
                    case "PhaseChanged":
                    case "PlayerCardsDrawn":
                    case "PlayerCardSubmitted":
                    case "PlayerDiscardStarted":
                    case "PlayerDiscardExecuted":
                    case "PlayerStepExecuted":
                    case "PlayerAutoMoved":
                    case "PlayerJoined":
                    case "PlayerLeft":
                    case "PlayerKicked":
                    case "PlayerFinished":
                    case "PlayerTimeoutElapsed":
                    case "RoomStarted":
                    case "RoomSettingsUpdated":
                    case "RoundAdvanced":
                    case "StepAdvanced":
                    // case "LogUpdated":
                        InvokeAsync(async () => {
                            await LoadGameData();
                            await LoadHandData();
                            StateHasChanged();
                        });
                        break;

                    // Events with specific, lighter-weight handling
                    case "PlayerBankUpdated":
                        // TODO: Implement specific handler if needed, for now, a full reload is safe
                        InvokeAsync(async () => {
                            await LoadGameData();
                            StateHasChanged();
                        });
                        break;

                    case "RoomEnded":
                        Console.WriteLine($"Game ended for room: {RoomId}");
                        InvokeAsync(async () => {
                            gameEnded = true;
                            showGameEndedOverlay = true;
                            DisposeTimers(); // Stop any running timers
                            await LoadGameData(); // Load final game state
                            StateHasChanged();
                        });
                        break;
                }
            });

            await hubConnection.StartAsync();
            isConnected = true;
            
            // Join game room
            if (hubConnection.State == HubConnectionState.Connected)
            {
                await hubConnection.InvokeAsync("JoinRoom", RoomId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SignalR connection failed: {ex.Message}");
        }
    }

    private async Task LoadGameData()
    {
        try
        {
            var response = await Http.GetFromJsonAsync<ApiSuccess<RoomStatusSnapshot>>($"/api/room/{RoomId}");
            
            if (response?.Data != null)
            {
                gameData = response.Data;
                roomName = gameData.Name ?? $"Game {RoomId[..8]}";
                
                // Check if game has ended based on status
                if (gameData.Status == "Ended" || gameData.Status == "Finished")
                {
                    gameEnded = true;
                    showGameEndedOverlay = true;
                    DisposeTimers(); // Stop any running timers
                }
                
                // Update phase from API response
                UpdateGamePhase(gameData.Phase);
                
                // Set current turn player (demo logic)
                if (gameData.Racers.Count > 0)
                {
                    currentTurnPlayer = gameData.CurrentTurnPlayerId;
                    isMyTurn = currentTurnPlayer == AuthService.PlayerId;
                }
                
                // Update time banks
                UpdatePlayerTimeBanks();
                
                // Update game logs from server with UUID-based deduplication
                if (gameData.Logs != null)
                {
                    UpdateGameLogsWithDeduplication(gameData.Logs);
                }
                
                isLoading = false;
            }
            else
            {
                // Only set gameData to null if game hasn't ended
                // This prevents "Room Not Found" when game ends
                if (!gameEnded)
                {
                    gameData = null;
                }
                isLoading = false;
            }

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load game data: {ex.Message}");
            // Only set gameData to null if game hasn't ended
            if (!gameEnded)
            {
                gameData = null;
            }
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void UpdateGamePhase(string phaseString)
    {
        var oldPhase = gamePhase;
        gamePhaseString = phaseString;
        
        // Try to parse the phase string to enum, fallback to default if parsing fails
        if (Enum.TryParse<Toko.Shared.Models.Phase>(phaseString, true, out var parsedPhase))
        {
            gamePhase = parsedPhase;
        }
        else
        {
            // Default fallback - don't throw, just use a safe default
            gamePhase = Toko.Shared.Models.Phase.CollectingCards;
            Console.WriteLine($"Unknown phase string: {phaseString}, using default CollectingCards");
        }
        
        // Reset parameter state when entering CollectingParams phase
        if (oldPhase != gamePhase && gamePhase == Phase.CollectingParams)
        {
            ResetParameterState();
        }
        
        // Reset discard selection when entering Discarding phase
        if (oldPhase != gamePhase && gamePhase == Phase.Discarding)
        {
            selectedDiscardCards.Clear();
            StartDiscardCountdown();
        }
    }

    private void ToggleCardSelection(CardDto card)
    {
        if (!IsCardPlayable(card)) return;

        // Since only one card can be selected at a time, toggle selection
        if (selectedCard == card.Id)
        {
            selectedCard = "";
        }
        else
        {
            selectedCard = card.Id;
        }
    }

    private bool IsCardPlayable(CardDto card)
    {
        return isMyTurn && gamePhase == Phase.CollectingCards && card.Type != "Junk";
    }

    private void HandleCardClick(CardDto card)
    {
        if (gamePhase == Phase.CollectingCards)
        {
            ToggleCardSelection(card);
        }
        else if (gamePhase == Phase.CollectingParams && gameData?.CurrentTurnCardType == "Repair")
        {
            // In repair phase, only allow selecting Junk cards from hand
            if (card.Type == "Junk")
            {
                ToggleJunkCardSelection(card.Id);
            }
        }
        else if (gamePhase == Phase.Discarding && IsPlayerInDiscardPhase())
        {
            ToggleDiscardCardSelection(card.Id);
        }
    }

    private string GetCardSelectionClass(CardDto card)
    {
        var classes = new List<string>();
        
        if (gamePhase == Phase.CollectingCards)
        {
            if (selectedCard == card.Id)
                classes.Add("selected");
            
            if (IsCardPlayable(card))
                classes.Add("playable");
            else
                classes.Add("disabled");
        }
        else if (isMyTurn && gamePhase == Phase.CollectingParams && gameData?.CurrentTurnCardType == "Repair")
        {
            // In repair phase, only Junk cards can be selected by the current player
            if (selectedJunkCards.Contains(card.Id))
                classes.Add("selected");
            
            if (card.Type == "Junk")
                classes.Add("playable");
            else
                classes.Add("disabled");
        }
        else if (gamePhase == Phase.Discarding && IsPlayerInDiscardPhase())
        {
            if (selectedDiscardCards.Contains(card.Id))
                classes.Add("selected");
            
            if (card.Type == "Junk")
                classes.Add("disabled");
            else
                classes.Add("playable");
        }
        else
        {
            classes.Add("disabled");
        }
        
        return string.Join(" ", classes);
    }

    private async Task PlaySelectedCard()
    {
        if (string.IsNullOrEmpty(selectedCard) || !isMyTurn) return;

        try
        {
            var request = new { CardId = selectedCard };
            await SendAsync(() => Http.PostAsJsonAsync($"/api/room/{RoomId}/submit-step-card", request));
            
            // Remove selected card from hand
            playerHand.RemoveAll(c => c.Id == selectedCard);
            selectedCard = "";
            
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to play card: {ex.Message}");
            await LoadGameData();
            await LoadHandData();
        }
    }

    private async Task DrawCards()
    {
        if (!isMyTurn) return;

        try
        {
            // Use the real API to draw cards
            var response = await SendAsync(() => Http.PostAsJsonAsync($"/api/room/{RoomId}/drawSkip", new { }));
            
            if (response.IsSuccessStatusCode)
            {
                // Reload hand data after drawing cards
                await LoadHandData();
                
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                Console.WriteLine($"Failed to draw cards: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to draw cards: {ex.Message}");
            await LoadGameData();
            await LoadHandData();
        }
    }

    private async Task ExecuteMove()
    {
        if (!isMyTurn || gamePhase != Phase.CollectingParams) return;

        try
        {
            // TODO: Implement real execution logic with API
            // For now, just add a log entry
            
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to execute move: {ex.Message}");
        }
    }

    // reload data on 400+ status codes
    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> action)
    {
        var response = await action();
        if (!response.IsSuccessStatusCode && (int)response.StatusCode >= 400)
        {
            await LoadGameData();
            await LoadHandData();
        }
        return response;
    }

    // Parameter collection methods
    private void SetChangeLaneEffect(int effect)
    {
        changeLaneEffect = effect;
    }

    private void SetShiftGearEffect(int effect)
    {
        shiftGearEffect = effect;
    }

    private void ToggleJunkCardSelection(string cardId)
    {
        if (selectedJunkCards.Contains(cardId))
        {
            selectedJunkCards.Clear();
        }
        else
        {
            selectedJunkCards.Clear();
            selectedJunkCards.Add(cardId);
        }
    }

    // Lane change direction helpers
    private RacerStatus? GetMyPlayer()
    {
        if (gameData?.Racers == null || string.IsNullOrEmpty(AuthService.PlayerId)) return null;
        return gameData.Racers.FirstOrDefault(r => r.Id == AuthService.PlayerId);
    }

    private string GetMyPlayerDirection()
    {
        var player = GetMyPlayer();
        if (player == null || gameData?.Map?.Segments == null) return "Up";
        
        if (player.Segment >= 0 && player.Segment < gameData.Map.Segments.Count)
        {
            return gameData.Map.Segments[player.Segment].Direction;
        }
        return "Up";
    }

    private (List<(string Text, int Value)>, string LayoutClass) GetLaneChangeButtonsAndLayout()
    {
        var direction = GetMyPlayerDirection();
        var baseDirection = GetBaseDirection(direction);
        
        // Direction mapping for lane change values and layout
        var (buttons, layoutClass) = baseDirection.ToLower() switch
        {
            "up" or "down" => (new List<(string, int)> { ("← Left", -1), ("Right →", 1) }, "lane-buttons-normal"),
            "left" or "right" => (new List<(string, int)> { ("↑ Up", 1), ("Down ↓", -1) }, "lane-buttons-vertical"),
            _ => (new List<(string, int)> { ("← Left", -1), ("Right →", 1) }, "lane-buttons-normal")
        };

        return (buttons, layoutClass);
    }

    private string GetBaseDirection(string direction)
    {
        // Extract base direction from corner directions
        return direction.ToLower() switch
        {
            "leftdown" or "leftup" => "left",
            "rightdown" or "rightup" => "right",
            "upright" or "upleft" => "up",
            "downright" or "downleft" => "down",
            _ => direction // Basic directions unchanged
        };
    }

    private bool IsParameterSubmissionReady()
    {
        if (!isMyTurn || gamePhase != Phase.CollectingParams) return false;
        
        return gameData?.CurrentTurnCardType switch
        {
            "ChangeLane" => changeLaneEffect == -1 || changeLaneEffect == 1,
            "ShiftGear" => shiftGearEffect == -1 || shiftGearEffect == 1,
            "Repair" => true, // Always ready for repair (can have empty selection)
            _ => false
        };
    }

    private async Task SubmitParameters()
    {
        if (!IsParameterSubmissionReady()) return;

        try
        {
            object execParameter = gameData?.CurrentTurnCardType switch
            {
                "ChangeLane" => new { Effect = changeLaneEffect, DiscardedCardIds = new List<string>() },
                "ShiftGear" => new { Effect = shiftGearEffect, DiscardedCardIds = new List<string>() },
                "Repair" => new { Effect = -1, DiscardedCardIds = selectedJunkCards.ToList() },
                _ => throw new InvalidOperationException($"Unknown card type: {gameData?.CurrentTurnCardType}")
            };

            var request = new { ExecParameter = execParameter };
            var response = await SendAsync(() => Http.PostAsJsonAsync($"/api/room/{RoomId}/submit-exec-param", request));

            if (response.IsSuccessStatusCode)
            {
                // Create detailed log message based on card type
                var logMessage = gameData?.CurrentTurnCardType switch
                {
                    "ChangeLane" => $"You submitted ChangeLane parameters: direction {(changeLaneEffect == -1 ? "Left" : "Right")}",
                    "ShiftGear" => $"You submitted ShiftGear parameters: {(shiftGearEffect == 1 ? "Shift Up" : "Shift Down")}",
                    "Repair" => $"You submitted Repair parameters: discarded {selectedJunkCards.Count} Junk cards",
                    _ => $"You submitted parameters for {gameData?.CurrentTurnCardType} card"
                };

                // Reset parameter state
                ResetParameterState();
                
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Failed to submit parameters: {response.StatusCode} - {errorContent}");
                
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to submit parameters: {ex.Message}");
            await LoadGameData();
            await LoadHandData();
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ResetParameterState()
    {
        changeLaneEffect = 1;
        shiftGearEffect = 1;
        selectedJunkCards.Clear();
    }

    // Discarding phase methods
    private bool IsPlayerInDiscardPhase()
    {
        var playerId = AuthService.PlayerId ?? "";
        var isInDiscardPhase = gamePhase == Phase.Discarding && 
                              gameData?.DiscardPendingPlayerIds?.Contains(playerId) == true;
        
        return isInDiscardPhase;
    }

    private bool HasJunkCardsSelected()
    {
        // Check if any selected cards are Junk cards
        return selectedDiscardCards.Any(cardId => 
            playerHand.Any(card => card.Id == cardId && card.Type == "Junk"));
    }

    private void ToggleDiscardCardSelection(string cardId)
    {
        // Find the card to check if it's a Junk card
        var card = playerHand.FirstOrDefault(c => c.Id == cardId);
        if (card == null || card.Type == "Junk")
        {
            return; // Cannot select Junk cards for discarding
        }

        if (selectedDiscardCards.Contains(cardId))
        {
            selectedDiscardCards.Remove(cardId);
        }
        else
        {
            selectedDiscardCards.Add(cardId);
        }
    }

    private async Task SubmitDiscardCards()
    {
        if (!IsPlayerInDiscardPhase()) return;

        try
        {
            var request = new { CardIds = selectedDiscardCards.ToList() };
            var response = await SendAsync(() => Http.PostAsJsonAsync($"/api/room/{RoomId}/discard-cards", request));

            if (response.IsSuccessStatusCode)
            {
                // Log the discard action
                var logMessage = selectedDiscardCards.Count > 0 
                    ? $"You discarded {selectedDiscardCards.Count} cards"
                    : "You chose not to discard any cards";
                
                // Clear selection
                selectedDiscardCards.Clear();
                
                // Reload hand and game data
                await LoadHandData();
                await LoadGameData();
                
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Failed to discard cards: {response.StatusCode} - {errorContent}");
                
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to discard cards: {ex.Message}");
            await LoadGameData();
            await LoadHandData();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SkipDiscard()
    {
        if (!IsPlayerInDiscardPhase()) return;

        try
        {
            // Submit empty card list to skip discarding
            var request = new { CardIds = new List<string>() };
            var response = await SendAsync(() => Http.PostAsJsonAsync($"/api/room/{RoomId}/discard-cards", request));

            if (response.IsSuccessStatusCode)
            {
                // Clear selection
                selectedDiscardCards.Clear();

                // Reload game data to reflect the change in discard status
                await LoadGameData();
                
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Failed to skip discard: {response.StatusCode} - {errorContent}");
                
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to skip discard: {ex.Message}");
            await LoadGameData();
            await LoadHandData();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LeaveGame()
    {
        try
        {
            await Http.PostAsync($"/api/room/{RoomId}/leave", null);
            
            if (hubConnection?.State == HubConnectionState.Connected)
            {
                await hubConnection.InvokeAsync("LeaveRoom", RoomId);
            }
            
            Navigation.NavigateTo("/");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to leave game: {ex.Message}");
        }
    }

    private void ShowGameMenu()
    {
        // TODO: Implement game menu
        Console.WriteLine("Game menu clicked");
    }

    private void GoHome()
    {
        Navigation.NavigateTo("/");
    }

    private void ViewRoom()
    {
        Navigation.NavigateTo($"/room/{RoomId}");
    }

    private void ReviewGame()
    {
        // Close the game ended overlay and show the game state
        showGameEndedOverlay = false;
        StateHasChanged();
    }

    // Helper methods
    private int GetPlayerPosition(string playerId)
    {
        // Get actual position from racer data
        if (gameData?.Racers == null) return 0;
        
        var racer = gameData.Racers.FirstOrDefault(r => r.Id == playerId);
        if (racer == null) return 0;
        
        // Calculate position based on Segment, Lane, and Tile
        // For now, use Segment as the primary position indicator
        return racer.Segment + 1;
    }

    private int GetPlayerGear(string playerId)
    {
        // Get gear from racer data
        if (gameData?.Racers == null) return 1;
        
        var racer = gameData.Racers.FirstOrDefault(r => r.Id == playerId);
        if (racer == null) return 1;
        
        // Return gear property
        return racer.Gear;
    }

    private string GetPlayerName(string? playerId)
    {
        if (string.IsNullOrEmpty(playerId) || gameData?.Racers == null) return "Unknown";
        return gameData.Racers.FirstOrDefault(r => r.Id == playerId)?.Name ?? "Unknown";
    }

    private string GetPlayerNameSafe(string? playerId)
    {
        // Try to get name from current racers first
        if (!string.IsNullOrEmpty(playerId) && gameData?.Racers != null)
        {
            var racer = gameData.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer != null)
            {
                return racer.Name;
            }
        }
        
        // If not found in racers, return a safe default
        return string.IsNullOrEmpty(playerId) ? "Unknown Player" : $"Player {playerId[..8]}";
    }

    private string GetPlayerInitials(string playerName)
    {
        var parts = playerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return playerName.Length >= 2 ? playerName[..2].ToUpper() : playerName.ToUpper();
    }

    private string GetPlayerColorClass(string playerId)
    {
        var index = gameData?.Racers?.FindIndex(r => r.Id == playerId) ?? 0;
        var colors = new[] { "color-red", "color-blue", "color-green", "color-yellow" };
        return colors[index % colors.Length];
    }

    private List<RacerStatus> GetPlayersAtPosition(int position)
    {
        if (gameData?.Racers == null) return new List<RacerStatus>();
        
        // Return racers at the specified segment position
        return gameData.Racers.Where(r => r.Segment == position).ToList();
    }

    private string GetCardValue(string cardType)
    {
        return cardType switch
        {
            "ChangeLane" => "L",
            "ShiftGear" => "G",
            "Junk" => "J",
            "Repair" => "R",
            _ => "?"
        };
    }

    // Card overlay helper methods
    private bool ShouldShowCardDisabledOverlay(CardDto card)
    {
        if (gamePhase == Phase.CollectingCards && card.Type == "Junk")
        {
            return true; // Junk cards cannot be played in normal card submission
        }
        // This special overlay logic should only apply to the player whose turn it is.
        else if (isMyTurn && gamePhase == Phase.CollectingParams && gameData?.CurrentTurnCardType == "Repair")
        {
            return card.Type != "Junk"; // Only Junk cards can be selected in repair phase
        }
        else if (gamePhase == Phase.Discarding && IsPlayerInDiscardPhase() && card.Type == "Junk")
        {
            return true; // Junk cards cannot be discarded
        }
        
        return false;
    }

    private string GetCardDisabledMessage(CardDto card)
    {
        if (gamePhase == Phase.CollectingCards && card.Type == "Junk")
        {
            return "Cannot play";
        }
        else if (gamePhase == Phase.CollectingParams && gameData?.CurrentTurnCardType == "Repair")
        {
            return card.Type != "Junk" ? "Cannot select" : "";
        }
        else if (gamePhase == Phase.Discarding && IsPlayerInDiscardPhase() && card.Type == "Junk")
        {
            return "Cannot discard";
        }
        
        return "Disabled";
    }

    public async ValueTask DisposeAsync()
    {
        DisposeTimers();
        
        if (hubConnection != null)
        {
            await hubConnection.DisposeAsync();
        }
    }

    private string GetPlayerTimeBankDisplay(string playerId)
    {
        if (playerTimeBanks.TryGetValue(playerId, out var timeBank))
        {
            // Handle negative time display correctly
            var isNegative = timeBank < 0;
            var absoluteTime = Math.Abs(timeBank);
            var minutes = (int)(absoluteTime / 60);
            var seconds = (int)(absoluteTime % 60);
            
            return isNegative 
                ? $"-{minutes:D2}:{seconds:D2}" 
                : $"{minutes:D2}:{seconds:D2}";
        }
        
        // Fallback to server data
        if (gameData?.Racers != null)
        {
            var racer = gameData.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer != null)
            {
                var minutes = (int)(racer.Bank / 60);
                var seconds = (int)(racer.Bank % 60);
                return $"{minutes:D2}:{seconds:D2}";
            }
        }
        
        return "05:00";
    }

    private void UpdatePlayerTimeBanks()
    {
        if (gameData?.Racers == null || gameEnded) return;

        lock (timerLock)
        {
            foreach (var racer in gameData.Racers)
            {
                // Stop existing timer if any
                if (playerTimers.TryGetValue(racer.Id, out var existingTimer))
                {
                    existingTimer?.Dispose();
                    playerTimers[racer.Id] = null;
                }

                // Sync with server bank data as the source of truth
                var remainingTime = racer.Bank;

                // If it's the current player's turn and we have a start time, calculate the actual remaining time
                if (racer.Id == currentTurnPlayer && gameData.TurnStartTimeUtc.HasValue)
                {
                    var elapsed = DateTime.UtcNow - gameData.TurnStartTimeUtc.Value;
                    remainingTime = racer.Bank - elapsed.TotalSeconds;
                }
                
                playerTimeBanks[racer.Id] = remainingTime;

                // Start a new timer for the active turn player to provide a smooth countdown UI on all clients
                if (racer.Id == currentTurnPlayer)
                {
                    var timer = new Timer(async _ => await CountDownPlayerTime(racer.Id), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                    playerTimers[racer.Id] = timer;
                }
            }
        }
    }

    private async Task CountDownPlayerTime(string playerId)
    {
        // Only count down if it's still the current player's turn
        if (playerId != currentTurnPlayer)
        {
            lock (timerLock)
            {
                if (playerTimers.TryGetValue(playerId, out var timer))
                {
                    timer?.Dispose();
                    playerTimers[playerId] = null;
                }
            }
            return;
        }

        lock (timerLock)
        {
            if (playerTimeBanks.TryGetValue(playerId, out var currentTime))
            {
                playerTimeBanks[playerId] = currentTime - 1;
            }
            
            // The timer is not stopped when it reaches zero, allowing it to go into negative values.
            // It will be naturally stopped and disposed when the turn changes.
        }

        await InvokeAsync(StateHasChanged);
    }

    private void DisposeTimers()
    {
        lock (timerLock)
        {
            foreach (var timer in playerTimers.Values)
            {
                timer?.Dispose();
            }
            playerTimers.Clear();
        }
        
        // Also dispose discard timer
        StopDiscardCountdown();
    }

    private void UpdateGameLogsWithDeduplication(List<TurnLog> newLogs)
    {
        bool hasNewLogs = false;
        
        // Add new logs that haven't been seen before
        foreach (var log in newLogs)
        {
            if (seenLogIds.Add(log.Id)) // Add returns true if item was not already in set
            {
                gameLogs.Add(log);
                hasNewLogs = true;
            }
        }
        
        // Only sort if we added new logs
        if (hasNewLogs)
        {
            gameLogs.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        }
    }

    private void StartDiscardCountdown()
    {
        lock (discardTimerLock)
        {
            // Stop existing timer if any
            discardTimer?.Dispose();
            
            // Start 15-second countdown
            discardCountdown = 15;
            
            discardTimer = new Timer(async _ => await CountDownDiscardTime(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    private async Task CountDownDiscardTime()
    {
        lock (discardTimerLock)
        {
            discardCountdown--;
            
            if (discardCountdown <= 0)
            {
                discardTimer?.Dispose();
                discardTimer = null;
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    private void StopDiscardCountdown()
    {
        lock (discardTimerLock)
        {
            discardTimer?.Dispose();
            discardTimer = null;
            discardCountdown = 0;
        }
    }
}
