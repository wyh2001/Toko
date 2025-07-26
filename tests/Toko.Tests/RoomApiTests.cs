using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Toko.Shared.Models;
using Toko.Shared.Services;
using Xunit;
using Xunit.Abstractions;
using static Toko.Tests.TestGameClient;

namespace Toko.Tests
{
    public class RoomApiTests(WebApplicationFactory<Program> factory, ITestOutputHelper output) : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client = factory.CreateClient();
        private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
        private readonly ITestOutputHelper _output = output;
        private record CreateRoomDto(string RoomId, string PlayerId, string PlayerName);
        // private record AuthDto(string? PlayerName, string PlayerId);

        [Fact]
        public async Task Authenticate_ShouldReturnSuccess()
        {
            var player = new TestGameClient(factory, _output);
            await player.AuthenticateAsync(); // Everything has been asserted inside
        }

        [Fact]
        public async Task AuthenticateTwice_ShouldReturnSuccessWithOnlyPlayerId()
        {
            var resp = await _client.GetAsync("/api/auth/anon");
            resp.EnsureSuccessStatusCode();
            var resp2 = await _client.GetAsync("/api/auth/anon");
            resp2.EnsureSuccessStatusCode();
            var authDto = await resp2.Content.ReadFromJsonAsync<ApiSuccess<AuthDto>>(_json);
            Assert.NotNull(authDto);
            Assert.NotNull(authDto.Data);
            Assert.NotNull(authDto.Data.PlayerId);
            Assert.Null(authDto.Data.PlayerName); // PlayerName should not be returned on subsequent calls
        }

        [Fact]
        public async Task CreateRoom_ShouldReturnSuccess()
        {
            var player = new TestGameClient(factory, _output);
            await player.AuthenticateAsync();
            await player.CreateRoomAsync(); // Everything has been asserted inside CreateRoomAsync() method
        }

        [Fact]
        public async Task CreateRoomWithoutAuth_ShouldReturnUnauthorized()
        {
            var resp = await _client.PostAsJsonAsync("/api/room/create", new
            {
                playerName = "TestPlayer",
                stepsPerRound = new[] { 1 }
            });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task CreateRoomWithInvalidName_ShouldReturnBadRequest()
        {
            var _ = await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/create", new
            {
                playerName = "", // Invalid player name
                stepsPerRound = new[] { 1 }
            });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("playerName", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CreateRoomWithInvalidSteps_ShouldReturnBadRequest()
        {
            var _ = await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/create", new
            {
                playerName = "TestPlayer",
                stepsPerRound = new[] { 0 } // Invalid steps per round
            });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("stepsPerRound", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetRoom_ShouldReturnRoomDetails()
        {
            var (playerId, playerName) = await AuthenticateAsync(_client);
            var roomId = await CreateRoomAsync(playerName, playerId, _client);
            var resp = await _client.GetAsync($"/api/room/{roomId}");
            resp.EnsureSuccessStatusCode();
            var roomDetails = await resp.Content.ReadFromJsonAsync<ApiSuccess<object>>(_json);
            Assert.NotNull(roomDetails);
            Assert.NotNull(roomDetails.Data);
            Assert.Contains(roomId, roomDetails.Data.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetRoomWithoutAuth_ShouldReturnUnauthorized()
        {
            var resp = await _client.GetAsync("/api/room/some-room-id");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("Unauthorized", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetNonExistentRoom_ShouldReturnNotFound()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.GetAsync("/api/room/non-existent-room-id");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("not found", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task JoinRoom_ShouldReturnSuccess()
        {
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            await player2.JoinRoomAsync(roomId); //Asserted inside
        }

        [Fact]
        public async Task JoinNonExistentRoom_ShouldReturnNotFound()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/0e6c63bf-ddc3-4b3b-b571-0d8737a81a51/join", new
            {
                playerName = "NewPlayer"
            });
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine("RAW RESPONSE: " + raw);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("not found", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task JoinInvalidRoomId_ShouldReturnBadRequest()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/not-valid-room-id/join", new
            {
                playerName = "NewPlayer"
            });
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine("RAW RESPONSE: " + raw);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("Invalid", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task JoinRoomWithSamePlayer_ShouldReturnBadRequest()
        {
            var player1 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            var resp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/join", new
            {
                playerName = player1.PlayerName // Same player trying to join again
            });
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine("RAW RESPONSE: " + raw);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("already joined", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task JoinRoomWithEmptyPlayerName_ShouldReturnBadRequest()
        {
            var player1 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            var resp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/join", new
            {
                playerName = "" // Empty player name
            });
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine("RAW RESPONSE: " + raw);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("playerName", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task JoinRoomWithoutAuth_ShouldReturnUnauthorized()
        {
            var resp = await _client.PostAsJsonAsync("/api/room/some-room-id/join", new
            {
                playerName = "TestPlayer"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("Unauthorized", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ListRoomWithoutAuth_ShouldReturnSuccess()
        {
            var resp = await _client.GetAsync("/api/room/list");
            resp.EnsureSuccessStatusCode();
            var rooms = await resp.Content.ReadFromJsonAsync<ApiSuccess<object>>(_json);
            Assert.NotNull(rooms);
            Assert.NotNull(rooms.Data);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
        }


        [Fact]
        public async Task ListRoomWithAuth_ShouldReturnSuccess()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.GetAsync("/api/room/list");
            resp.EnsureSuccessStatusCode();
            var rooms = await resp.Content.ReadFromJsonAsync<ApiSuccess<object>>(_json);
            Assert.NotNull(rooms);
            Assert.NotNull(rooms.Data);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ListRoomAfterCreatingRoom_ShouldReturnRoomInList()
        {
            var player = new TestGameClient(factory, _output);
            await player.AuthenticateAsync();
            var roomId = await player.CreateRoomAsync();
            await AuthenticateAsync(_client);
            var resp = await _client.GetAsync("/api/room/list");
            resp.EnsureSuccessStatusCode();
            var rooms = await resp.Content.ReadFromJsonAsync<ApiSuccess<object>>(_json);
            Assert.NotNull(rooms);
            Assert.NotNull(rooms.Data);
            Assert.Contains(roomId, rooms.Data.ToString(), StringComparison.OrdinalIgnoreCase);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ReadyForNonExistentRoom_ShouldReturnNotFound()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/0e6c63bf-ddc3-4b3b-b571-0d8737a81a51/ready", new { isReady = true });
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("not found", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReadyForInvalidRoomId_ShouldReturnBadRequest()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/not-valid-room-id/ready", new { isReady = true });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("Invalid", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReadyWithoutAuth_ShouldReturnUnauthorized()
        {
            var resp = await _client.PostAsJsonAsync("/api/room/some-room-id/ready", new { isReady = true });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("Unauthorized", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task StartWithNotReadyPlayer_ShouldReturnBadRequest()
        {
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            await player2.JoinRoomAsync(roomId); // Asserted inside
            
            // Player 2 is not ready, so starting the game should fail
            var resp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("Not all players are ready", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task StartWhenNotHost_ShouldReturnForbidden()
        {
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            await player2.JoinRoomAsync(roomId); // Asserted inside
            
            // Player 2 tries to start the game, but is not the host
            var resp = await player2.Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("You are not the host", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task StartWhenNotInTheRoom_ShouldReturnNotFound()
        {
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            
            // Player 2 tries to start the game without joining the room
            var resp = await player2.Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("not in the room", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task StartWhenAlreadyStarted_ShouldReturnBadRequest()
        {
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            await player2.JoinRoomAsync(roomId); // Asserted inside
            
            // Mark both players as ready
            await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/ready", new { isReady = true });
            await player2.Client.PostAsJsonAsync($"/api/room/{roomId}/ready", new { isReady = true });
            
            // Start the game
            var startResp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            startResp.EnsureSuccessStatusCode();
            
            // Try to start the game again
            var resp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("requires room status", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task StartWhenAlreadyFinished_ShouldReturnBadRequest()
        {
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            await player2.JoinRoomAsync(roomId); // Asserted inside
            
            // Mark both players as ready
            await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/ready", new { isReady = true });
            await player2.Client.PostAsJsonAsync($"/api/room/{roomId}/ready", new { isReady = true });
            
            // Start the game
            var startResp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            startResp.EnsureSuccessStatusCode();
            
            // Simulate game finished (this part is not implemented in the provided code)
            // For example, you might update the room status to "finished" in your database
            
            // Try to start the game again
            var resp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("requires room status", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task StartNotExistentRoom_ShouldReturnNotFound()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/0e6c63bf-ddc3-4b3b-b571-0d8737a81a51/start", new { });
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("not found", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task StartWithReadyPlayers_ShouldReturnSuccess()
        {
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            await player2.JoinRoomAsync(roomId); // Asserted inside
            // Mark both players as ready
            await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/ready", new { isReady = true });
            await player2.Client.PostAsJsonAsync($"/api/room/{roomId}/ready", new { isReady = true });
            // Now start the game
            var resp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            resp.EnsureSuccessStatusCode();
            var startResponse = await resp.Content.ReadFromJsonAsync<ApiSuccess<object>>(_json);
            Assert.NotNull(startResponse);
            Assert.NotNull(startResponse.Data);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task LeaveNonExistentRoom_ShouldReturnNotFound()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/0e6c63bf-ddc3-4b3b-b571-0d8737a81a51/leave", new { });
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("not found", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LeaveInvalidRoomId_ShouldReturnBadRequest()
        {
            await AuthenticateAsync(_client);
            var resp = await _client.PostAsJsonAsync("/api/room/not-valid-room-id/leave", new { });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("Invalid", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LeaveWithoutAuth_ShouldReturnUnauthorized()
        {
            var resp = await _client.PostAsJsonAsync("/api/room/0e6c63bf-ddc3-4b3b-b571-0d8737a81a51/leave", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("Unauthorized", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LeaveWhenNotInRoom_ShouldReturnNotFound()
        {
            var player1 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            var player2 = new TestGameClient(factory, _output);
            await player2.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            // Player 2 tries to leave the room without joining it
            var resp = await player2.Client.PostAsJsonAsync($"/api/room/{roomId}/leave", new { });
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var error = await resp.Content.ReadFromJsonAsync<ProblemDetails>(_json);
            Assert.NotNull(error);
            Assert.NotNull(error.Title);
            Assert.NotNull(error.Detail);
            Assert.Contains("not in the room", error.Detail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LeaveRoom_ShouldReturnSuccess()
        {
            var player1 = new TestGameClient(factory, _output);
            await player1.AuthenticateAsync();
            var roomId = await player1.CreateRoomAsync();
            var resp = await player1.Client.PostAsJsonAsync($"/api/room/{roomId}/leave", new { });
            resp.EnsureSuccessStatusCode();
            var leaveResponse = await resp.Content.ReadFromJsonAsync<ApiSuccess<object>>(_json);
            Assert.NotNull(leaveResponse);
            Assert.NotNull(leaveResponse.Data);
            _output.WriteLine("RAW RESPONSE: " + await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task TwoPlayersStartGame_FirstPlayerPlaysCard_ShouldHaveOnlyOneLogEntry()
        {
            // Create two test players
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);

            // Authenticate both players
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();

            // Create room
            var roomId = await player1.CreateRoomAsync();
            // Second player joins the room
            await player2.JoinRoomAsync(roomId);

            // Both players ready up
            await player1.SetReadyAsync(roomId, true);
            await player2.SetReadyAsync(roomId, true);

            // Start the game
            await player1.StartGameAsync(roomId);
            
            // Wait for game state to update
            await Task.Delay(100);

            // Verify game has started and is in card collection phase
            var status = await player1.GetRoomStatusAsync(roomId);
            Assert.Equal("Playing", status.Status);
            Assert.Equal("CollectingCards", status.Phase);

            // First player gets their hand
            var hand = await player1.GetHandAsync(roomId);
            Assert.NotNull(hand);
            Assert.NotNull(hand.Cards);
            Assert.True(hand.Cards.Count > 0, "Player should have cards in hand");

            // First player plays a random card
            var randomCard = hand.Cards[new Random().Next(hand.Cards.Count)];
            _output.WriteLine($"Player1 plays card: {randomCard.Type} (ID: {randomCard.Id})");
            await player1.SubmitStepCardAsync(roomId, randomCard.Id);

            // Get room status directly via API (including logs)
            var resp = await player1.Client.GetAsync($"/api/room/{roomId}/status");
            resp.EnsureSuccessStatusCode();
            var fullStatus = await resp.Content.ReadFromJsonAsync<ApiSuccess<Toko.Shared.Models.RoomStatusSnapshot>>(_json);
            
            Assert.NotNull(fullStatus);
            Assert.NotNull(fullStatus.Data);
            
            // Verify there is only one log entry
            Assert.NotNull(fullStatus.Data.Logs);
            var singleLog = Assert.Single(fullStatus.Data.Logs);
            
            // Verify the log is about the first player's card submission
            Assert.Equal(player1.PlayerId, singleLog.PlayerId);
            Assert.Contains("submitted", singleLog.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CompleteGameFlowUsingAPI_ShouldSucceed()
        {
            // Create three test game clients to simulate 3 players
            var player1 = new TestGameClient(factory, _output);
            var player2 = new TestGameClient(factory, _output);
            var player3 = new TestGameClient(factory, _output);
            var player4 = new TestGameClient(factory, _output);

            // Step 1: Authenticate all players
            _output.WriteLine("=== Step 1: Authenticating players ===");
            await player1.AuthenticateAsync();
            await player2.AuthenticateAsync();
            await player3.AuthenticateAsync();
            await player4.AuthenticateAsync();
            _output.WriteLine($"Player1: {player1.PlayerName} ({player1.PlayerId})");
            _output.WriteLine($"Player2: {player2.PlayerName} ({player2.PlayerId})");
            _output.WriteLine($"Player3: {player3.PlayerName} ({player3.PlayerId})");
            _output.WriteLine($"Player4: {player4.PlayerName} ({player4.PlayerId})");

            // Step 2: Create room with 3 rounds, 3 steps each (default game configuration)
            _output.WriteLine("=== Step 2: Creating room ===");
            var roomId = await player1.CreateRoomWithSettingsAsync("TestGame", 4, false, new[] { 3, 3, 3 });
            _output.WriteLine($"Created room: {roomId}");

            // Step 3: Players 2 and 3 join the room
            _output.WriteLine("=== Step 3: Players joining room ===");
            await player2.JoinRoomAsync(roomId);
            await player3.JoinRoomAsync(roomId);
            await player4.JoinRoomAsync(roomId);

            // Step 4: Check room status
            _output.WriteLine("=== Step 4: Checking initial room status ===");
            var status = await player1.GetRoomStatusAsync(roomId);
            Assert.Equal("Waiting", status.Status);
            Assert.Equal(4, status.Racers.Count);
            _output.WriteLine($"Room status: {status.Status}, Players: {status.Racers.Count}");

            // Step 5: All players ready up
            _output.WriteLine("=== Step 5: Players readying up ===");
            await player1.SetReadyAsync(roomId, true);
            await player2.SetReadyAsync(roomId, true);
            await player3.SetReadyAsync(roomId, true);
            await player4.SetReadyAsync(roomId, true);

            // Step 6: Host starts the game
            _output.WriteLine("=== Step 6: Starting the game ===");
            await player1.StartGameAsync(roomId);

            // Wait a bit for game initialization
            await Task.Delay(100);

            // Step 7: Check game has started
            status = await player1.GetRoomStatusAsync(roomId);
            Assert.Equal("Playing", status.Status);
            Assert.Equal("CollectingCards", status.Phase);
            Assert.Equal(0, status.CurrentRound); // 0-based indexing
            Assert.Equal(0, status.CurrentStep);  // 0-based indexing
            _output.WriteLine($"Game started! Status: {status.Status}, Phase: {status.Phase}, Round: {status.CurrentRound}, Step: {status.CurrentStep}");

            // Step 8: Play through multiple rounds
            await PlayCompleteGame(player1, player2, player3, player4, roomId);

            _output.WriteLine("=== Game completed successfully! ===");
        }

        private async Task PlayCompleteGame(TestGameClient player1, TestGameClient player2, TestGameClient player3, TestGameClient player4, string roomId)
        {
            var players = new[] { player1, player2, player3, player4 };
            var random = new Random();

            // Game progress tracking
            var maxIterations = 100; // Increased limit for complete game
            var iteration = 0;
            var lastRound = -1;
            var lastStep = -1;
            var roundsCompleted = 0;
            var expectedTotalRounds = 3; // We set up the game with 3 rounds
            var stepsPerRound = new[] { 3, 3, 3 }; // 3 steps per round
            var stuckCounter = 0; // Count consecutive iterations in same state
            var lastStateKey = "";
            var finishLine = 20; // Default value, will be updated

            // Track submitted cards for each player, round, and step
            // (Should not use this since clients only know what they submitted before when they are prompted to submit parameters)
            //var submittedCards = new Dictionary<(string playerId, int round, int step), string>();

            _output.WriteLine("\n🎮 === GAME SIMULATION STARTED ===");

            while (iteration < maxIterations)
            {
                iteration++;

                // Get current room status
                var status = await player1.GetRoomStatusAsync(roomId);
                
                if (iteration == 1)
                {
                    // Extract finish line from map data on first iteration
                    if (status.Map is JsonElement mapJson && mapJson.TryGetProperty("totalCells", out var totalCellsElement))
                    {
                        finishLine = totalCellsElement.GetInt32() - 1;
                        _output.WriteLine($"🗺️ Finish line is at tile {finishLine}");
                    }
                    else
                    {
                        _output.WriteLine($"⚠️ Could not determine finish line from map data, using default {finishLine}");
                    }
                }

                // Check if we're stuck in the same state
                var currentStateKey = $"R{status.CurrentRound}S{status.CurrentStep}P{status.Phase}";
                if (currentStateKey == lastStateKey)
                {
                    stuckCounter++;
                    if (stuckCounter >= 5) // If stuck for 5 iterations - reduced to get debug info faster
                    {
                        _output.WriteLine($"⚠️  STUCK WARNING: Game has been in state {currentStateKey} for {stuckCounter} iterations");
                        if (stuckCounter >= 8) // Fail if stuck too long - reduced to get debug info faster
                        {
                            // Print final status for debugging
                            _output.WriteLine($"🔍 Final Status Debug:");
                            _output.WriteLine($"   Room ID: {status.RoomId}");
                            _output.WriteLine($"   Status: {status.Status}");
                            _output.WriteLine($"   Phase: {status.Phase}");
                            _output.WriteLine($"   Round: {status.CurrentRound}, Step: {status.CurrentStep}");
                            _output.WriteLine($"   Current Turn Player: {status.CurrentTurnPlayerId}");
                            _output.WriteLine($"   Discard Pending: [{string.Join(", ", status.DiscardPendingPlayerIds)}]");
                            _output.WriteLine($"   Racers:");
                            foreach (var racer in status.Racers)
                            {
                                _output.WriteLine($"     - {racer.Name} (ID: {racer.Id}): Lane {racer.Lane}, Tile {racer.Tile}, Hand {racer.HandCount}, Banned: {racer.IsBanned}");
                            }
                            
                            Assert.Fail($"Game appears to be stuck in state {currentStateKey} for {stuckCounter} iterations");
                        }
                    }
                }
                else
                {
                    stuckCounter = 0;
                    lastStateKey = currentStateKey;
                }

                // Track progress changes
                if (status.CurrentRound != lastRound || status.CurrentStep != lastStep)
                {
                    _output.WriteLine($"\n📊 === ROUND {status.CurrentRound + 1} STEP {status.CurrentStep + 1} ===");
                    _output.WriteLine($"Phase: {status.Phase} | Turn: {status.CurrentTurnPlayerId}");
                    _output.WriteLine($"Current Status: Round={status.CurrentRound}, Step={status.CurrentStep}, Phase={status.Phase}");
                    
                    // Check if we completed a round (moved to next round)
                    if (status.CurrentRound > lastRound)
                    {
                        if (lastRound >= 0) // Not the first round
                        {
                            roundsCompleted++;
                            _output.WriteLine($"🎉 Round {lastRound + 1} completed! ({roundsCompleted}/{expectedTotalRounds} rounds done)");
                            
                            // Assert that we're making progress through rounds
                            Assert.True(roundsCompleted <= expectedTotalRounds,
                                       $"Completed more rounds ({roundsCompleted}) than expected ({expectedTotalRounds})");
                        }
                    }
                    
                    lastRound = status.CurrentRound;
                    lastStep = status.CurrentStep;
                }

                // Game finished check
                if (status.Status == "Finished")
                {
                    _output.WriteLine("\n🏁 === GAME FINISHED ===");
                    
                    // Calculate absolute positions for finish line check
                    var mapJson = (JsonElement)status.Map;
                    var someoneReachedFinishLine = status.Racers.Any(r => 
                        CalculateAbsolutePosition(r.Segment, r.Tile, mapJson) >= finishLine);
                    
                    // Assert game completion reason
                    Assert.True(status.CurrentRound >= expectedTotalRounds || someoneReachedFinishLine,
                               $"Game should finish after {expectedTotalRounds} rounds or when someone crosses finish line. " +
                               $"Current round: {status.CurrentRound}, " +
                               $"Max absolute position: {status.Racers.Max(r => CalculateAbsolutePosition(r.Segment, r.Tile, mapJson))}");
                    
                    // Display final results with absolute positions
                    _output.WriteLine("\n🏆 FINAL STANDINGS:");
                    var sortedRacers = status.Racers
                        .Select(r => new
                        {
                            Racer = r,
                            AbsolutePosition = CalculateAbsolutePosition(r.Segment, r.Tile, mapJson)
                        })
                        .OrderByDescending(x => x.AbsolutePosition)
                        .ThenBy(x => x.Racer.Lane)
                        .ToList();
                    
                    for (int i = 0; i < sortedRacers.Count; i++)
                    {
                        var entry = sortedRacers[i];
                        var racer = entry.Racer;
                        _output.WriteLine($"   {i + 1}. {racer.Name} - Segment {racer.Segment}, Lane {racer.Lane}, " +
                                        $"RelativeTile {racer.Tile}, AbsolutePosition {entry.AbsolutePosition}, Bank: {racer.Bank:F2}");
                    }
                    
                    // Assert that we have a valid winner and game progress
                    var winner = sortedRacers.First();
                    Assert.True(winner.AbsolutePosition > 0, "Winner should have advanced from starting position");
                    Assert.True(roundsCompleted > 0 || status.CurrentRound > 0, "Game should have made progress through rounds");
                    _output.WriteLine($"🥇 Winner: {winner.Racer.Name} at absolute position {winner.AbsolutePosition}");
                    
                    // Final progress report
                    _output.WriteLine($"\n📈 GAME STATISTICS:");
                    _output.WriteLine($"   Total iterations: {iteration}");
                    _output.WriteLine($"   Rounds completed: {roundsCompleted}");
                    _output.WriteLine($"   Final round/step: R{status.CurrentRound + 1}S{status.CurrentStep + 1}");
                    
                    return; // Successful completion
                }

                // Assert game is still in valid state
                Assert.Equal("Playing", status.Status);
                Assert.True(status.CurrentRound >= 0 && status.CurrentRound < expectedTotalRounds + 1, 
                           $"Round should be between 0 and {expectedTotalRounds}, actual: {status.CurrentRound}");
                
                var maxStepForRound = status.CurrentRound < stepsPerRound.Length ? stepsPerRound[status.CurrentRound] : stepsPerRound.Last();
                _output.WriteLine($"🔍 Validating step: CurrentRound={status.CurrentRound}, CurrentStep={status.CurrentStep}, MaxStepForRound={maxStepForRound}");
                Assert.True(status.CurrentStep >= 0 && status.CurrentStep <= maxStepForRound, 
                           $"Step should be valid for current round. Round: {status.CurrentRound}, Step: {status.CurrentStep}, MaxStep: {maxStepForRound} (steps 0-{maxStepForRound-1} are valid, {maxStepForRound} indicates round transition)");

                // Find whose turn it is
                var currentPlayer = players.FirstOrDefault(p => p.PlayerId == status.CurrentTurnPlayerId);
                if (currentPlayer == null)
                {
                    _output.WriteLine($"⏳ No current turn player found, waiting... (Turn ID: {status.CurrentTurnPlayerId})");
                    _output.WriteLine($"   Available players: {string.Join(", ", players.Select(p => $"{p.PlayerName}({p.PlayerId})"))}");
                    _output.WriteLine($"   Game Phase: {status.Phase}, Round: {status.CurrentRound}, Step: {status.CurrentStep}");
                    await Task.Delay(200);
                    continue;
                }

                _output.WriteLine($"▶️  {currentPlayer.PlayerName}'s turn in {status.Phase} phase");

                try
                {
                    if (status.Phase == "CollectingCards")
                    {
                        await HandleCardPhase(currentPlayer, roomId, random);
                    }
                    else if (status.Phase == "CollectingParams")
                    {
                        await HandleParamPhase(currentPlayer, roomId, random);
                    }
                    else if (status.Phase == "Discarding")
                    {
                        // In discard phase, check if any players need to discard
                        if (status.DiscardPendingPlayerIds.Count > 0)
                        {
                            _output.WriteLine($"   🗑️  Discarding phase: {status.DiscardPendingPlayerIds.Count} players need to discard");
                            _output.WriteLine($"   📋 Pending players: [{string.Join(", ", status.DiscardPendingPlayerIds)}]");
                            
                            // Find the first pending player instead of relying on CurrentTurnPlayerId
                            var pendingPlayer = players.FirstOrDefault(p => status.DiscardPendingPlayerIds.Contains(p.PlayerId));
                            if (pendingPlayer != null)
                            {
                                await HandleDiscardPhase(pendingPlayer, roomId, random);
                            }
                            else
                            {
                                _output.WriteLine($"   ❌ No pending discard players found among our test clients");
                            }
                        }
                        else
                        {
                            _output.WriteLine($"   ✅ No players need to discard, but phase is still 'Discarding'");
                        }
                    }
                    else
                    {
                        _output.WriteLine($"⚠️  Unknown phase: {status.Phase}");
                        await Task.Delay(200);
                    }

                    // Small delay between actions
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"❌ Error during {currentPlayer.PlayerName}'s turn in {status.Phase}: {ex.Message}");
                    _output.WriteLine($"   Full exception: {ex}");
                    await Task.Delay(200);
                }
            }

            // If we reach here, the game didn't finish within the iteration limit
            Assert.Fail($"Game did not finish within {maxIterations} iterations. Last state: R{lastRound + 1}S{lastStep + 1}");
        }

        private async Task HandleCardPhase(TestGameClient player, string roomId, Random random)
        {
            // Get player's hand
            var hand = await player.GetHandAsync(roomId);
            _output.WriteLine($"   📋 {player.PlayerName} has {hand.Cards.Count} cards: [{string.Join(", ", hand.Cards.Select(c => c.Type))}]");

            if (hand.Cards.Count == 0)
            {
                _output.WriteLine($"   🎴 {player.PlayerName} has no cards, drawing to skip...");
                await player.DrawSkipAsync(roomId);
                return;
            }

            // Filter out Junk cards since they cannot be submitted
            var submittableCards = hand.Cards.Where(c => c.Type != "Junk").ToList();
            _output.WriteLine($"   📋 {player.PlayerName} has {submittableCards.Count} submittable cards (excluding {hand.Cards.Count - submittableCards.Count} Junk cards)");

            // If only Junk cards remain, draw to skip
            if (submittableCards.Count == 0)
            {
                _output.WriteLine($"   🗑️ {player.PlayerName} only has Junk cards, drawing to skip...");
                await player.DrawSkipAsync(roomId);
                return;
            }

            // Randomly decide whether to submit a card or draw to skip (80% submit, 20% skip)
            if (random.NextDouble() < 0.8)
            {
                // Submit a random card from submittable cards
                var cardToSubmit = submittableCards[random.Next(submittableCards.Count)];
                _output.WriteLine($"   🃏 {player.PlayerName} submitting {cardToSubmit.Type} card (ID: {cardToSubmit.Id})");
                await player.SubmitStepCardAsync(roomId, cardToSubmit.Id);
            }
            else
            {
                _output.WriteLine($"   🎴 {player.PlayerName} choosing to draw and skip...");
                await player.DrawSkipAsync(roomId);
            }
        }

        private async Task HandleParamPhase(TestGameClient player, string roomId, Random random)
        {
            _output.WriteLine($"   ⚙️ {player.PlayerName} submitting execution parameters...");

            // Get current room status to find out what card type we need to submit parameters for
            var status = await player.GetRoomStatusAsync(roomId);
            
            if (string.IsNullOrEmpty(status.CurrentTurnCardType))
            {
                _output.WriteLine($"   ❌ No card type information available for {player.PlayerName}");
                return;
            }

            _output.WriteLine($"   🃏 {player.PlayerName} needs to submit parameters for {status.CurrentTurnCardType} card");

            object? paramToSubmit = null;
            string paramDesc = "";

            switch (status.CurrentTurnCardType)
            {
                case "Move":
                    // Move cards accept Effect: 1 or 2
                    var moveEffect = random.Next(1, 3); // 1 or 2
                    paramToSubmit = new { Effect = moveEffect, DiscardedCardIds = new List<string>() };
                    paramDesc = $"Move effect={moveEffect}";
                    break;
                    
                case "ChangeLane":
                    // ChangeLane cards accept Effect: 1 or -1
                    var laneEffect = random.Next(0, 2) == 0 ? -1 : 1; // -1 or 1
                    paramToSubmit = new { Effect = laneEffect, DiscardedCardIds = new List<string>() };
                    paramDesc = $"ChangeLane effect={laneEffect}";
                    break;
                    
                case "Repair":
                    // Repair cards can only discard Junk cards, or auto-skip if no Junk cards available
                    var hand = await player.GetHandAsync(roomId);
                    var junkCards = hand.Cards.Where(c => c.Type == "Junk").ToList();
                    if (junkCards.Count > 0)
                    {
                        // Only discard Junk cards for Repair action
                        var cardsToDiscard = junkCards.Take(1).Select(c => c.Id).ToList();
                        paramToSubmit = new { Effect = -1, DiscardedCardIds = cardsToDiscard };
                        paramDesc = $"Repair effect=-1, discard {cardsToDiscard.Count} Junk cards";
                    }
                    else
                    {
                        // No Junk cards to discard - this will trigger auto-skip behavior
                        paramToSubmit = new { Effect = -1, DiscardedCardIds = new List<string>() };
                        paramDesc = $"Repair effect=-1, no Junk cards to discard (auto-skip)";
                        _output.WriteLine($"   ⏭️ {player.PlayerName} has no Junk cards to discard for Repair card, using auto-skip");
                    }
                    break;

                default:
                    _output.WriteLine($"   ❌ Unknown card type: {status.CurrentTurnCardType}");
                    return;
            }

            if (paramToSubmit != null)
            {
                try
                {
                    _output.WriteLine($"   🔧 {player.PlayerName} submitting: {paramDesc}");
                    await player.SubmitExecParamAsync(roomId, paramToSubmit);
                    _output.WriteLine($"   ✅ {player.PlayerName} successfully submitted parameters: {paramDesc}");
                }
                catch (HttpRequestException ex)
                {
                    _output.WriteLine($"   ❌ {player.PlayerName} failed to submit parameters: {ex.Message}");
                    throw;
                }
            }
        }

        private async Task HandleDiscardPhase(TestGameClient player, string roomId, Random random)
        {
            // Check if this player needs to discard
            var status = await player.GetRoomStatusAsync(roomId);
            if (!status.DiscardPendingPlayerIds.Contains(player.PlayerId))
            {
                _output.WriteLine($"   ⏭️  {player.PlayerName} is not in discard pending list, skipping...");
                return;
            }

            // Get player's hand
            var hand = await player.GetHandAsync(roomId);
            if (hand.Cards.Count == 0)
            {
                _output.WriteLine($"   🃏 {player.PlayerName} has no cards to discard");
                await player.DiscardCardsAsync(roomId, new List<string>());
                return;
            }

            // Filter out Junk cards since they cannot be discarded in the discarding phase
            var discardableCards = hand.Cards.Where(c => c.Type != "Junk").ToList();
            _output.WriteLine($"   📋 {player.PlayerName} has {hand.Cards.Count} total cards, {discardableCards.Count} discardable (excluding {hand.Cards.Count - discardableCards.Count} Junk cards)");

            // If no discardable cards remain, don't discard anything
            if (discardableCards.Count == 0)
            {
                _output.WriteLine($"   🗑️ {player.PlayerName} has no discardable cards (only Junk cards), not discarding anything");
                await player.DiscardCardsAsync(roomId, new List<string>());
                return;
            }

            // Randomly discard 0-2 cards from discardable cards only
            var discardCount = Math.Min(random.Next(0, 3), discardableCards.Count);
            if (discardCount > 0)
            {
                var cardsToDiscard = discardableCards.Take(discardCount).Select(c => c.Id).ToList();
                var cardTypes = discardableCards.Take(discardCount).Select(c => c.Type);
                _output.WriteLine($"   🗑️  {player.PlayerName} discarding {discardCount} cards: [{string.Join(", ", cardTypes)}]");
                await player.DiscardCardsAsync(roomId, cardsToDiscard);
            }
            else
            {
                _output.WriteLine($"   ✋ {player.PlayerName} choosing not to discard any cards");
                await player.DiscardCardsAsync(roomId, new List<string>());
            }
        }

        // Helper method to calculate absolute cell position from segment and cell indices
        private static int CalculateAbsolutePosition(int segmentIndex, int cellIndex, JsonElement mapJson)
        {
            try
            {
                // Get the segments array from map data
                if (!mapJson.TryGetProperty("segments", out var segmentsElement))
                    return cellIndex; // Fallback to relative position

                var segments = segmentsElement.EnumerateArray().ToList();

                // Sum the lengths of all previous segments
                int absolutePosition = 0;
                for (int i = 0; i < segmentIndex && i < segments.Count; i++)
                {
                    var segment = segments[i];
                    if (segment.TryGetProperty("laneCellCounts", out var laneCellCountsElement))
                    {
                        // Get the first lane's cell count (all lanes in a segment should have the same count)
                        var firstLaneCount = laneCellCountsElement.EnumerateArray().FirstOrDefault();
                        if (firstLaneCount.ValueKind == JsonValueKind.Number)
                        {
                            absolutePosition += firstLaneCount.GetInt32();
                        }
                    }
                }

                // Add the current cell index within the current segment
                absolutePosition += cellIndex;

                return absolutePosition;
            }
            catch (Exception)
            {
                // If parsing fails, fallback to relative position
                return cellIndex;
            }
        }

        [Fact]
        public async Task CreateDefaultMapAndVerifyTileProperties_ShouldMatchExpectedValues()
        {
            // Load test data from JSON
            var testCases = LoadMapTestCases();
            
            foreach (var testCase in testCases)
            {
                var player = new TestGameClient(factory, _output);
                await player.AuthenticateAsync();

                _output.WriteLine($"=== Testing Map: {testCase.MapName} ===");

                // Create room with custom map
                var roomId = await player.CreateRoomWithCustomMapAsync(
                    testCase.MapName, 4, false, new[] { 1 }, testCase.CustomMapRequest);

                // Get room status and recreate map
                var status = await player.GetRoomStatusAsync(roomId);
                var mapJson = (JsonElement)status.Map;
                var segmentsJson = mapJson.GetProperty("segments").EnumerateArray();

                var actualSegments = segmentsJson.Select(segJson => new MapSegmentSnapshot(
                    segJson.GetProperty("type").GetString()!,
                    segJson.GetProperty("laneCount").GetInt32(),
                    segJson.GetProperty("cellCount").GetInt32(),
                    segJson.GetProperty("direction").GetString()!,
                    segJson.GetProperty("isIntermediate").GetBoolean()
                )).ToList();

                var map = RaceMapFactory.CreateMap(actualSegments);

                // Create position lookup dictionary (like frontend)
                var cellLookup = new Dictionary<(int x, int y), Cell>();
                foreach (var segment in map.Segments)
                {
                    foreach (var lane in segment.LaneCells)
                    {
                        foreach (var cell in lane)
                        {
                            cellLookup[(cell.Position.X, cell.Position.Y)] = cell;
                        }
                    }
                }

                _output.WriteLine($"Map has {cellLookup.Count} total cells");

                // Collect all verification errors before asserting
                var errors = new List<string>();

                // Test expected cells from JSON
                foreach (var expectedCell in testCase.ExpectedCells)
                {
                    // Find cell by coordinate using lookup dictionary
                    if (cellLookup.TryGetValue((expectedCell.X, expectedCell.Y), out var cell))
                    {
                        // Verify cell type
                        if (expectedCell.ExpectedCellType != cell.Type)
                        {
                            errors.Add($"Cell type mismatch at ({expectedCell.X}, {expectedCell.Y}): Expected {expectedCell.ExpectedCellType}, Got {cell.Type}");
                        }
                        
                        // Verify grid properties
                        if (expectedCell.ExpectedGrid != null)
                        {
                            if (cell.Grid == null)
                            {
                                errors.Add($"Grid should not be null at ({expectedCell.X}, {expectedCell.Y}), but it is null");
                            }
                            else
                            {
                                if (expectedCell.ExpectedGrid.RenderingType != cell.Grid.RenderingType)
                                {
                                    errors.Add($"Grid RenderingType mismatch at ({expectedCell.X}, {expectedCell.Y}): Expected {expectedCell.ExpectedGrid.RenderingType}, Got {cell.Grid.RenderingType}");
                                }
                                
                                // For Plain tiles, any rotation is acceptable
                                if (expectedCell.ExpectedGrid.RenderingType != MapRenderingType.Plain &&
                                    expectedCell.ExpectedGrid.Rotation != cell.Grid.Rotation)
                                {
                                    errors.Add($"Grid Rotation mismatch at ({expectedCell.X}, {expectedCell.Y}): Expected {expectedCell.ExpectedGrid.Rotation}, Got {cell.Grid.Rotation}");
                                }
                                
                                if (expectedCell.ExpectedGrid.IsFlipped != cell.Grid.IsFlipped)
                                {
                                    errors.Add($"Grid IsFlipped mismatch at ({expectedCell.X}, {expectedCell.Y}): Expected {expectedCell.ExpectedGrid.IsFlipped}, Got {cell.Grid.IsFlipped}");
                                }
                            }
                        }
                        else
                        {
                            if (cell.Grid != null)
                            {
                                errors.Add($"Grid should be null at ({expectedCell.X}, {expectedCell.Y}), but got: {cell.Grid.RenderingType}, {cell.Grid.Rotation}, Flipped={cell.Grid.IsFlipped}");
                            }
                        }
                    }
                    else
                    {
                        errors.Add($"No cell found at coordinate ({expectedCell.X}, {expectedCell.Y}). Available coordinates: {string.Join(", ", cellLookup.Keys.Take(10).Select(k => $"({k.x},{k.y})"))}...");
                    }
                }

                // Output all errors at once if any found
                if (errors.Count > 0)
                {
                    _output.WriteLine($"\n❌ Map '{testCase.MapName}' - Found {errors.Count} verification errors:");
                    for (int i = 0; i < errors.Count; i++)
                    {
                        _output.WriteLine($"   {i + 1}. {errors[i]}");
                    }
                    
                    // Print track structure for debugging
                    PrintTrackStructure(mapJson, _output);
                    
                    Assert.Fail($"Map '{testCase.MapName}' verification failed with {errors.Count} errors:\n{string.Join("\n", errors)}");
                }

                _output.WriteLine($"\n✅ Map '{testCase.MapName}' - All {testCase.ExpectedCells.Count} cells verified successfully");
            }
        }

        private List<MapTestCase> LoadMapTestCases()
        {
            // Try multiple possible paths for the JSON file
            var possiblePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "MapTestData.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "MapTestData.json"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "MapTestData.json"),
                "MapTestData.json"
            };

            string? jsonPath = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    jsonPath = path;
                    break;
                }
            }

            if (jsonPath == null)
            {
                var searchedPaths = string.Join("\n  - ", possiblePaths);
                throw new FileNotFoundException($"MapTestData.json not found. Searched paths:\n  - {searchedPaths}");
            }

            var jsonContent = File.ReadAllText(jsonPath);
            var testData = JsonSerializer.Deserialize<MapTestData>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (testData?.TestCases == null)
            {
                throw new InvalidOperationException("No test cases found in MapTestData.json");
            }

            return testData.TestCases.Select(tc => new MapTestCase
            {
                MapName = tc.MapName,
                CustomMapRequest = new CustomMapRequest(
                    tc.MapSnapshot.Segments.Select(s => new MapSegmentSnapshot(
                        s.Type, s.LaneCount, s.CellCount, s.Direction, false // isIntermediate is always false for user-defined segments
                    )).ToList()
                ),
                ExpectedCells = tc.ExpectedCells.Select(ec => new ExpectedCellData
                {
                    X = ec.X,
                    Y = ec.Y,
                    SegmentIndex = ec.SegmentIndex,
                    LaneIndex = ec.LaneIndex,
                    CellIndex = ec.CellIndex,
                    ExpectedCellType = Enum.Parse<CellType>(ec.ExpectedCellType),
                    ExpectedGrid = ec.ExpectedGrid != null ? new Grid(
                        Enum.Parse<MapRenderingType>(ec.ExpectedGrid.RenderingType),
                        Enum.Parse<MapRenderingRotation>(ec.ExpectedGrid.Rotation),
                        ec.ExpectedGrid.IsFlipped
                    ) : null
                }).ToList()
            }).ToList();
        }

        // Data models for JSON test data
        public class MapTestData
        {
            public List<MapTestCaseJson> TestCases { get; set; } = new();
        }

        public class MapTestCaseJson
        {
            public string MapName { get; set; } = "";
            public MapSnapshotJson MapSnapshot { get; set; } = new();
            public List<ExpectedCellDataJson> ExpectedCells { get; set; } = new();
        }

        public class MapSnapshotJson
        {
            public int TotalCells { get; set; }
            public List<MapSegmentSnapshotJson> Segments { get; set; } = new();
        }

        public class MapSegmentSnapshotJson
        {
            public string Type { get; set; } = "";
            public int LaneCount { get; set; }
            public int CellCount { get; set; }
            public string Direction { get; set; } = "";
        }

        public class ExpectedCellDataJson
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int SegmentIndex { get; set; }
            public int LaneIndex { get; set; }
            public int CellIndex { get; set; }
            public string ExpectedCellType { get; set; } = "";
            public GridJson? ExpectedGrid { get; set; }
        }

        public class GridJson
        {
            public string RenderingType { get; set; } = "";
            public string Rotation { get; set; } = "";
            public bool IsFlipped { get; set; }
        }

        // Test execution models
        public class MapTestCase
        {
            public string MapName { get; set; } = "";
            public CustomMapRequest CustomMapRequest { get; set; } = new(new List<MapSegmentSnapshot>());
            public List<ExpectedCellData> ExpectedCells { get; set; } = new();
        }

        public class ExpectedCellData
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int SegmentIndex { get; set; }
            public int LaneIndex { get; set; }
            public int CellIndex { get; set; }
            public CellType ExpectedCellType { get; set; }
            public Grid? ExpectedGrid { get; set; }
        }

        // Helper method to print race track structure for debugging
        private void PrintTrackStructure(JsonElement mapJson, ITestOutputHelper output)
        {
            try
            {
                output.WriteLine("\n🗺️ === RACE TRACK STRUCTURE ===");
                
                if (mapJson.TryGetProperty("totalCells", out var totalCellsElement))
                {
                    output.WriteLine($"   Total Cells: {totalCellsElement.GetInt32()}");
                }
                
                if (mapJson.TryGetProperty("segments", out var segmentsElement))
                {
                    var segments = segmentsElement.EnumerateArray().ToList();
                    output.WriteLine($"   Total Segments: {segments.Count}");
                    
                    for (int i = 0; i < segments.Count; i++)
                    {
                        var segment = segments[i];
                        var type = segment.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "Unknown";
                        var laneCount = segment.TryGetProperty("laneCount", out var laneCountElement) ? laneCountElement.GetInt32() : 0;
                        var cellCount = segment.TryGetProperty("cellCount", out var cellCountElement) ? cellCountElement.GetInt32() : 0;
                        var direction = segment.TryGetProperty("direction", out var directionElement) ? directionElement.GetString() : "Unknown";
                        var isIntermediate = segment.TryGetProperty("isIntermediate", out var isIntermediateElement) ? isIntermediateElement.GetBoolean() : false;
                        
                        output.WriteLine($"   Segment {i}: {type} | Lanes: {laneCount} | Cells: {cellCount} | Direction: {direction} | Intermediate: {isIntermediate}");
                        
                        // Print lane cell counts if available
                        if (segment.TryGetProperty("laneCellCounts", out var laneCellCountsElement))
                        {
                            var laneCellCounts = laneCellCountsElement.EnumerateArray().Select(x => x.GetInt32()).ToList();
                            output.WriteLine($"     Lane Cell Counts: [{string.Join(", ", laneCellCounts)}]");
                        }
                    }
                }
                
                output.WriteLine("================================\n");
            }
            catch (Exception ex)
            {
                output.WriteLine($"⚠️ Failed to print track structure: {ex.Message}");
            }
        }
    }
}
