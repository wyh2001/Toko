using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using static Toko.Controllers.RoomController;
using static Toko.IntegrationTests.TestGameClient;

namespace Toko.IntegrationTests
{
    public class RoomApiTests(WebApplicationFactory<Program> factory, ITestOutputHelper output) : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client = factory.CreateClient();
        private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
        private readonly ITestOutputHelper _output = output;
        private record CreateRoomDto(string RoomId, string PlayerId, string PlayerName);
        private record AuthDto(string PlayerName, string PlayerId);

        [Fact]
        public async Task Authenticate_ShouldReturnSuccess()
        {
            var player = new TestGameClient(factory, _output);
            await player.AuthenticateAsync(); // Everything has been asserted inside
        }

        [Fact]
        public async Task AuthenticateTwice_ShouldReturnNoContent()
        {
            var resp = await _client.GetAsync("/api/auth/anon");
            resp.EnsureSuccessStatusCode();
            var resp2 = await _client.GetAsync("/api/auth/anon");
            Assert.Equal(HttpStatusCode.NoContent, resp2.StatusCode);
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

        //[Fact]
    }
}

