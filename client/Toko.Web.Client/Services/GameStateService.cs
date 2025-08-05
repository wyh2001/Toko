using Microsoft.AspNetCore.Components;
using Toko.Shared.Models;
using Toko.Web.Client.Services;

namespace Toko.Web.Client.Services
{
    public sealed class GameStateService(
        IRaceHubService hub,
        IGameApiService api,
        IAuthenticationService auth,
        NavigationManager nav) : IAsyncDisposable
    {
        private readonly IRaceHubService HubService = hub;
        private readonly IGameApiService ApiService = api;
        private readonly IAuthenticationService AuthService = auth;
        private readonly NavigationManager Navigation = nav;

        public string RoomId { get; set; } = string.Empty;

        public bool IsLoading { get; private set; } = true;
        public RoomStatusSnapshot? GameData { get; private set; }
        public string RoomName { get; private set; } = "";
        public Phase GamePhase { get; private set; } = Phase.CollectingCards;
        public string GamePhaseString { get; private set; } = "CollectingCards";
        public string? CurrentTurnPlayer { get; private set; }
        public bool IsMyTurn { get; private set; } = false;
        public bool GameEnded { get; private set; } = false;
        public bool ShowGameEndedOverlay { get; private set; } = false;
        public bool IsConnected { get; private set; } = true;
        public bool IsReconnecting { get; private set; } = false;

        public int DisplayedRound => GameData == null ? 0 : (GameEnded ? GameData.TotalRounds : GameData.CurrentRound + 1);

        public IReadOnlyList<CardDto> PlayerHand => _playerHand;
        public string SelectedCard { get; private set; } = "";
        public IReadOnlyList<TurnLog> GameLogs => _gameLogs;

        // Parameter collection state
        public int ChangeLaneEffect { get; set; } = 1;
        public int ShiftGearEffect { get; set; } = 1;
        public IReadOnlyList<string> SelectedJunkCards => _selectedJunkCards;
        public IReadOnlyList<CardDto> JunkCards => _junkCards;

        // Discarding phase state
        public IReadOnlyList<string> SelectedDiscardCards => _selectedDiscardCards;
        public IReadOnlyList<CardDto> DiscardableCards => _discardableCards;

        // Time bank state
        public IReadOnlyDictionary<string, double> PlayerTimeBanks => _playerTimeBanks;

        // Discard phase countdown timer
        public int DiscardCountdown { get; private set; } = 0;

        // Private backing fields
        private readonly List<CardDto> _playerHand = new();
        private readonly List<TurnLog> _gameLogs = new();
        private readonly HashSet<Guid> _seenLogIds = new(); // Track seen log UUIDs for deduplication
        private readonly List<string> _selectedJunkCards = new();
        private readonly List<CardDto> _junkCards = new();
        private readonly List<string> _selectedDiscardCards = new();
        private readonly List<CardDto> _discardableCards = new();
        private readonly Dictionary<string, double> _playerTimeBanks = new();
        private readonly Dictionary<string, Timer?> _playerTimers = new();
        private readonly object _timerLock = new object();

        // Discard phase countdown timer
        private Timer? _discardTimer;
        private readonly object _discardTimerLock = new object();

        public event Action? OnChange;

        public async Task InitializeAsync(string roomId)
        {
            RoomId = roomId;
            await AuthService.EnsureAuthenticatedAsync();
            await InitializeServices();
            await LoadGameData();
            await LoadHandData();
            IsLoading = false;
            NotifyStateChanged();
        }

        private async Task InitializeServices()
        {
            await HubService.ConnectAsync(Navigation.ToAbsoluteUri("/racehub").ToString());
            await HubService.JoinRoomAsync(RoomId);

            HubService.ConnectionStateChanged += OnConnectionStateChanged;
            HubService.ReconnectingStateChanged += OnReconnectingStateChanged;
            HubService.GameEventReceived += OnGameEventReceived;
        }

        private void OnConnectionStateChanged(bool connected)
        {
            IsConnected = connected;
            NotifyStateChanged();
        }

        private void OnReconnectingStateChanged(bool reconnecting)
        {
            IsReconnecting = reconnecting;
            NotifyStateChanged();
        }

        private async void OnGameEventReceived(GameEvent evt)
        {
            await HandleGameEventAsync(evt.EventName, evt.EventData);
        }

        private async Task HandleGameEventAsync(string eventName, object eventData)
        {
            Console.WriteLine($"Game event received: {eventName}");

            switch (eventName)
            {
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
                    await LoadGameData();
                    await LoadHandData();
                    break;

                case "PlayerBankUpdated":
                    await LoadGameData();
                    break;

                case "RoomEnded":
                    Console.WriteLine($"Game ended for room: {RoomId}");
                    GameEnded = true;
                    ShowGameEndedOverlay = true;
                    DisposeTimers();
                    await LoadGameData();
                    break;
            }

            // Unified notification at the end
            NotifyStateChanged();
        }

        private async Task LoadHandData()
        {
            try
            {
                if (GameData?.Status != "Playing") return;

                var cards = await ApiService.GetHandAsync(RoomId);
                _playerHand.Clear();
                _playerHand.AddRange(cards);

                _junkCards.Clear();
                _junkCards.AddRange(_playerHand.Where(c => c.Type == "Junk"));
                _discardableCards.Clear();
                _discardableCards.AddRange(_playerHand.Where(c => c.Type != "Junk"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load hand data: {ex.Message}");
            }
        }

        private async Task LoadGameData()
        {
            try
            {
                var snapshot = await ApiService.GetRoomStatusAsync(RoomId);

                if (snapshot != null)
                {
                    GameData = snapshot;
                    RoomName = GameData.Name ?? $"Game {RoomId[..8]}";

                    if (GameData.Status == "Ended" || GameData.Status == "Finished")
                    {
                        GameEnded = true;
                        ShowGameEndedOverlay = true;
                        DisposeTimers();
                    }

                    UpdateGamePhase(GameData.Phase);

                    if (GameData.Racers.Count > 0)
                    {
                        CurrentTurnPlayer = GameData.CurrentTurnPlayerId;
                        IsMyTurn = CurrentTurnPlayer == AuthService.PlayerId;
                    }

                    UpdatePlayerTimeBanks();

                    if (GameData.Logs != null)
                    {
                        UpdateGameLogsWithDeduplication(GameData.Logs);
                    }

                    IsLoading = false;
                }
                else
                {
                    if (!GameEnded)
                    {
                        GameData = null;
                    }
                    IsLoading = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load game data: {ex.Message}");
                // Only set gameData to null if game hasn't ended
                if (!GameEnded)
                {
                    GameData = null;
                }
                IsLoading = false;
            }
        }

        private void UpdateGamePhase(string phaseString)
        {
            var oldPhase = GamePhase;
            GamePhaseString = phaseString;

            // Try to parse the phase string to enum, fallback to default if parsing fails
            if (Enum.TryParse<Toko.Shared.Models.Phase>(phaseString, true, out var parsedPhase))
            {
                GamePhase = parsedPhase;
            }
            else
            {
                // Default fallback - don't throw, just use a safe default
                GamePhase = Toko.Shared.Models.Phase.CollectingCards;
                Console.WriteLine($"Unknown phase string: {phaseString}, using default CollectingCards");
            }

            // Reset parameter state when entering CollectingParams phase
            if (oldPhase != GamePhase && GamePhase == Phase.CollectingParams)
            {
                ResetParameterState();
            }

            // Reset discard selection when entering Discarding phase
            if (oldPhase != GamePhase && GamePhase == Phase.Discarding)
            {
                _selectedDiscardCards.Clear();
                StartDiscardCountdown();
            }
        }

        private void ToggleCardSelection(CardDto card)
        {
            if (!IsCardPlayable(card)) return;

            // Since only one card can be selected at a time, toggle selection
            if (SelectedCard == card.Id)
            {
                SelectedCard = "";
            }
            else
            {
                SelectedCard = card.Id;
            }
        }

        public bool IsCardPlayable(CardDto card)
        {
            return IsMyTurn && GamePhase == Phase.CollectingCards && card.Type != "Junk";
        }

        public void HandleCardClick(CardDto card)
        {
            if (GamePhase == Phase.CollectingCards)
            {
                ToggleCardSelection(card);
            }
            else if (GamePhase == Phase.CollectingParams && GameData?.CurrentTurnCardType == "Repair")
            {
                // In repair phase, only allow selecting Junk cards from hand
                if (card.Type == "Junk")
                {
                    ToggleJunkCardSelection(card.Id);
                }
            }
            else if (GamePhase == Phase.Discarding && IsPlayerInDiscardPhase())
            {
                ToggleDiscardCardSelection(card.Id);
            }
        }

        public string GetCardSelectionClass(CardDto card)
        {
            var classes = new List<string>();

            if (GamePhase == Phase.CollectingCards)
            {
                if (SelectedCard == card.Id)
                    classes.Add("selected");

                if (IsCardPlayable(card))
                    classes.Add("playable");
                else
                    classes.Add("disabled");
            }
            else if (IsMyTurn && GamePhase == Phase.CollectingParams && GameData?.CurrentTurnCardType == "Repair")
            {
                // In repair phase, only Junk cards can be selected by the current player
                if (SelectedJunkCards.Contains(card.Id))
                    classes.Add("selected");

                if (card.Type == "Junk")
                    classes.Add("playable");
                else
                    classes.Add("disabled");
            }
            else if (GamePhase == Phase.Discarding && IsPlayerInDiscardPhase())
            {
                if (SelectedDiscardCards.Contains(card.Id))
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

        public async Task PlaySelectedCard()
        {
            if (string.IsNullOrEmpty(SelectedCard) || !IsMyTurn) return;

            try
            {
                var success = await ApiService.PlayCardAsync(RoomId, SelectedCard);
                if (success)
                {
                    _playerHand.RemoveAll(c => c.Id == SelectedCard);
                    SelectedCard = "";
                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to play card: {ex.Message}");
                await LoadGameData();
                await LoadHandData();
            }
        }

        public async Task DrawCards()
        {
            if (!IsMyTurn) return;

            try
            {
                var success = await ApiService.DrawCardsAsync(RoomId);
                if (success)
                {
                    await LoadHandData();
                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to draw cards: {ex.Message}");
            }
        }

        private void ExecuteMove()
        {
            if (!IsMyTurn || GamePhase != Phase.CollectingParams) return;

            try
            {
                // TODO: Implement real execution logic with API
                // For now, just add a log entry

                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to execute move: {ex.Message}");
            }
        }

        // Parameter collection methods
        public void SetChangeLaneEffect(int effect)
        {
            ChangeLaneEffect = effect;
        }

        public void SetShiftGearEffect(int effect)
        {
            ShiftGearEffect = effect;
        }

        private void ToggleJunkCardSelection(string cardId)
        {
            if (_selectedJunkCards.Contains(cardId))
            {
                _selectedJunkCards.Clear();
            }
            else
            {
                _selectedJunkCards.Clear();
                _selectedJunkCards.Add(cardId);
            }
        }

        // Lane change direction helpers
        private RacerStatus? GetMyPlayer()
        {
            if (GameData?.Racers == null || string.IsNullOrEmpty(AuthService.PlayerId)) return null;
            return GameData.Racers.FirstOrDefault(r => r.Id == AuthService.PlayerId);
        }

        private string GetMyPlayerDirection()
        {
            var player = GetMyPlayer();
            if (player == null || GameData?.Map?.Segments == null) return "Up";

            if (player.Segment >= 0 && player.Segment < GameData.Map.Segments.Count)
            {
                return GameData.Map.Segments[player.Segment].Direction;
            }
            return "Up";
        }

        public (List<(string Text, int Value)>, string LayoutClass) GetLaneChangeButtonsAndLayout()
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

        public bool IsParameterSubmissionReady()
        {
            if (!IsMyTurn || GamePhase != Phase.CollectingParams) return false;

            return GameData?.CurrentTurnCardType switch
            {
                "ChangeLane" => ChangeLaneEffect == -1 || ChangeLaneEffect == 1,
                "ShiftGear" => ShiftGearEffect == -1 || ShiftGearEffect == 1,
                "Repair" => true, // Always ready for repair (can have empty selection)
                _ => false
            };
        }

        public async Task SubmitParameters()
        {
            if (!IsParameterSubmissionReady()) return;

            try
            {
                object execParameter = GameData?.CurrentTurnCardType switch
                {
                    "ChangeLane" => new { Effect = ChangeLaneEffect, DiscardedCardIds = new List<string>() },
                    "ShiftGear" => new { Effect = ShiftGearEffect, DiscardedCardIds = new List<string>() },
                    "Repair" => new { Effect = -1, DiscardedCardIds = SelectedJunkCards.ToList() },
                    _ => throw new InvalidOperationException($"Unknown card type: {GameData?.CurrentTurnCardType}")
                };

                var success = await ApiService.SubmitParametersAsync(RoomId, new { ExecParameter = execParameter });

                if (success)
                {
                    ResetParameterState();
                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to submit parameters: {ex.Message}");
                await LoadGameData();
                await LoadHandData();
            }
        }

        private void ResetParameterState()
        {
            ChangeLaneEffect = 1;
            ShiftGearEffect = 1;
            _selectedJunkCards.Clear();
        }

        // Discarding phase methods
        public bool IsPlayerInDiscardPhase()
        {
            var playerId = AuthService.PlayerId ?? "";
            var isInDiscardPhase = GamePhase == Phase.Discarding &&
                                  GameData?.DiscardPendingPlayerIds?.Contains(playerId) == true;

            return isInDiscardPhase;
        }

        public bool HasJunkCardsSelected()
        {
            // Check if any selected cards are Junk cards
            return SelectedDiscardCards.Any(cardId =>
                PlayerHand.Any(card => card.Id == cardId && card.Type == "Junk"));
        }

        private void ToggleDiscardCardSelection(string cardId)
        {
            // Find the card to check if it's a Junk card
            var card = PlayerHand.FirstOrDefault(c => c.Id == cardId);
            if (card == null || card.Type == "Junk")
            {
                return; // Cannot select Junk cards for discarding
            }

            if (_selectedDiscardCards.Contains(cardId))
            {
                _selectedDiscardCards.Remove(cardId);
            }
            else
            {
                _selectedDiscardCards.Add(cardId);
            }
        }

        public async Task SubmitDiscardCards()
        {
            if (!IsPlayerInDiscardPhase()) return;

            try
            {
                var success = await ApiService.SubmitDiscardCardsAsync(RoomId, SelectedDiscardCards.ToList());

                if (success)
                {
                    _selectedDiscardCards.Clear();
                    await LoadHandData();
                    await LoadGameData();
                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to submit discard cards: {ex.Message}");
            }
        }

        public async Task SkipDiscard()
        {
            if (!IsPlayerInDiscardPhase()) return;

            try
            {
                var success = await ApiService.SkipDiscardAsync(RoomId);

                if (success)
                {
                    _selectedDiscardCards.Clear();
                    await LoadGameData();
                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to skip discard: {ex.Message}");
            }
        }

        public async Task LeaveGame()
        {
            try
            {
                await ApiService.LeaveGameAsync(RoomId);
                await HubService.LeaveRoomAsync(RoomId);
                Navigation.NavigateTo("/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to leave game: {ex.Message}");
            }
        }

        public void ShowGameMenu()
        {
            // TODO: Implement game menu
            Console.WriteLine("Game menu clicked");
        }

        public void GoHome()
        {
            Navigation.NavigateTo("/");
        }

        public void ViewRoom()
        {
            Navigation.NavigateTo($"/room/{RoomId}");
        }

        public void ReviewGame()
        {
            // Close the game ended overlay and show the game state
            ShowGameEndedOverlay = false;
            NotifyStateChanged();
        }

        // Helper methods
        public int GetPlayerPosition(string playerId)
        {
            // Get actual position from racer data
            if (GameData?.Racers == null) return 0;

            var racer = GameData.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer == null) return 0;

            // Calculate position based on Segment, Lane, and Tile
            // For now, use Segment as the primary position indicator
            return racer.Segment + 1;
        }

        public int GetPlayerGear(string playerId)
        {
            // Get gear from racer data
            if (GameData?.Racers == null) return 1;

            var racer = GameData.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer == null) return 1;

            // Return gear property
            return racer.Gear;
        }

        public string GetPlayerName(string? playerId)
        {
            if (string.IsNullOrEmpty(playerId) || GameData?.Racers == null) return "Unknown";
            return GameData.Racers.FirstOrDefault(r => r.Id == playerId)?.Name ?? "Unknown";
        }

        public string GetPlayerNameSafe(string? playerId)
        {
            // Try to get name from current racers first
            if (!string.IsNullOrEmpty(playerId) && GameData?.Racers != null)
            {
                var racer = GameData.Racers.FirstOrDefault(r => r.Id == playerId);
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
            var index = GameData?.Racers?.FindIndex(r => r.Id == playerId) ?? 0;
            var colors = new[] { "color-red", "color-blue", "color-green", "color-yellow" };
            return colors[index % colors.Length];
        }

        private List<RacerStatus> GetPlayersAtPosition(int position)
        {
            if (GameData?.Racers == null) return new List<RacerStatus>();

            // Return racers at the specified segment position
            return GameData.Racers.Where(r => r.Segment == position).ToList();
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
        public bool ShouldShowCardDisabledOverlay(CardDto card)
        {
            if (GamePhase == Phase.CollectingCards && card.Type == "Junk")
            {
                return true; // Junk cards cannot be played in normal card submission
            }
            // This special overlay logic should only apply to the player whose turn it is.
            else if (IsMyTurn && GamePhase == Phase.CollectingParams && GameData?.CurrentTurnCardType == "Repair")
            {
                return card.Type != "Junk"; // Only Junk cards can be selected in repair phase
            }
            else if (GamePhase == Phase.Discarding && IsPlayerInDiscardPhase() && card.Type == "Junk")
            {
                return true; // Junk cards cannot be discarded
            }

            return false;
        }

        public string GetCardDisabledMessage(CardDto card)
        {
            if (GamePhase == Phase.CollectingCards && card.Type == "Junk")
            {
                return "Cannot play";
            }
            else if (GamePhase == Phase.CollectingParams && GameData?.CurrentTurnCardType == "Repair")
            {
                return card.Type != "Junk" ? "Cannot select" : "";
            }
            else if (GamePhase == Phase.Discarding && IsPlayerInDiscardPhase() && card.Type == "Junk")
            {
                return "Cannot discard";
            }

            return "Disabled";
        }

        public async ValueTask DisposeAsync()
        {
            // Unsubscribe from events to prevent memory leaks
            HubService.ConnectionStateChanged -= OnConnectionStateChanged;
            HubService.ReconnectingStateChanged -= OnReconnectingStateChanged;
            HubService.GameEventReceived -= OnGameEventReceived;

            DisposeTimers();
            await HubService.DisposeAsync();
        }

        public string GetPlayerTimeBankDisplay(string playerId)
        {
            if (_playerTimeBanks.TryGetValue(playerId, out var timeBank))
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
            if (GameData?.Racers != null)
            {
                var racer = GameData.Racers.FirstOrDefault(r => r.Id == playerId);
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
            if (GameData?.Racers == null || GameEnded) return;

            lock (_timerLock)
            {
                foreach (var racer in GameData.Racers)
                {
                    // Stop existing timer if any
                    if (_playerTimers.TryGetValue(racer.Id, out var existingTimer))
                    {
                        existingTimer?.Dispose();
                        _playerTimers[racer.Id] = null;
                    }

                    // Sync with server bank data as the source of truth
                    var remainingTime = racer.Bank;

                    // If it's the current player's turn and we have a start time, calculate the actual remaining time
                    if (racer.Id == CurrentTurnPlayer && GameData.TurnStartTimeUtc.HasValue)
                    {
                        var elapsed = DateTime.UtcNow - GameData.TurnStartTimeUtc.Value;
                        remainingTime = racer.Bank - elapsed.TotalSeconds;
                    }

                    _playerTimeBanks[racer.Id] = remainingTime;

                    // Start a new timer for the active turn player to provide a smooth countdown UI on all clients
                    if (racer.Id == CurrentTurnPlayer)
                    {
                        var timer = new Timer(_ => CountDownPlayerTime(racer.Id), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                        _playerTimers[racer.Id] = timer;
                    }
                }
            }
        }

        private void CountDownPlayerTime(string playerId)
        {
            // Only count down if it's still the current player's turn
            if (playerId != CurrentTurnPlayer)
            {
                lock (_timerLock)
                {
                    if (_playerTimers.TryGetValue(playerId, out var timer))
                    {
                        timer?.Dispose();
                        _playerTimers[playerId] = null;
                    }
                }
                return;
            }

            lock (_timerLock)
            {
                if (_playerTimeBanks.TryGetValue(playerId, out var currentTime))
                {
                    _playerTimeBanks[playerId] = currentTime - 1;
                }

                // The timer is not stopped when it reaches zero, allowing it to go into negative values.
                // It will be naturally stopped and disposed when the turn changes.
            }

            NotifyStateChanged();
        }

        private void DisposeTimers()
        {
            lock (_timerLock)
            {
                foreach (var timer in _playerTimers.Values)
                {
                    timer?.Dispose();
                }
                _playerTimers.Clear();
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
                if (_seenLogIds.Add(log.Id)) // Add returns true if item was not already in set
                {
                    _gameLogs.Add(log);
                    hasNewLogs = true;
                }
            }

            // Only sort if we added new logs
            if (hasNewLogs)
            {
                _gameLogs.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            }
        }

        private void StartDiscardCountdown()
        {
            lock (_discardTimerLock)
            {
                // Stop existing timer if any
                _discardTimer?.Dispose();

                // Start 15-second countdown
                DiscardCountdown = 15;

                _discardTimer = new Timer(_ => CountDownDiscardTime(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
        }

        private void CountDownDiscardTime()
        {
            lock (_discardTimerLock)
            {
                DiscardCountdown--;

                if (DiscardCountdown <= 0)
                {
                    _discardTimer?.Dispose();
                    _discardTimer = null;
                }
            }

            NotifyStateChanged();
        }

        private void StopDiscardCountdown()
        {
            lock (_discardTimerLock)
            {
                _discardTimer?.Dispose();
                _discardTimer = null;
                DiscardCountdown = 0;
            }
        }
        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
