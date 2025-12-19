using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Toko.Shared.Models;
using Xunit.Abstractions;
//using static Toko.Controllers.RoomController;

namespace Toko.Tests
{
    public sealed class TestGameClient(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public HttpClient Client { get; } = factory.CreateClient();
        public string PlayerId { get; private set; } = "";
        public string PlayerName { get; set; } = "";
        private readonly ITestOutputHelper _output = output;


        public record HandResponse
        {
            public string RoomId { get; init; } = "";
            public string PlayerId { get; init; } = "";
            public List<CardDto> Cards { get; init; } = new();
        }

        // only use once, or return no content 204
        public async Task AuthenticateAsync()
        {
            var resp = await Client.GetAsync("/api/auth/anon");
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<ApiSuccess<AuthDto>>(Json);
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine(raw);
            Assert.NotNull(body);
            Assert.NotNull(body.Data);
            Assert.NotNull(body.Data.PlayerId);
            Assert.NotNull(body.Data.PlayerName);
            PlayerId = body.Data.PlayerId;
            PlayerName = body.Data.PlayerName;
        }

        public static async Task<(string, string)> AuthenticateAsync(HttpClient client)
        {
            var resp = await client.GetAsync("/api/auth/anon");
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<ApiSuccess<AuthDto>>(Json);
            Assert.NotNull(body);
            Assert.NotNull(body.Data);
            Assert.NotNull(body.Data.PlayerId);
            Assert.NotNull(body.Data.PlayerName);
            return (body.Data.PlayerId, body.Data.PlayerName);
        }

        public async Task<string> CreateRoomAsync()
        {
            var resp = await Client.PostAsJsonAsync("/api/room/create", new
            {
                playerName = PlayerName,
                stepsPerRound = new[] { 1 }
            });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine(raw);
            var wrapper = await resp.Content.ReadFromJsonAsync<ApiSuccess<CreateRoomDto>>(Json);
            Assert.NotNull(wrapper);
            Assert.NotNull(wrapper.Data);
            Assert.NotNull(wrapper.Data.RoomId);
            Assert.True(Guid.TryParse(wrapper.Data.RoomId, out _), "RoomId must be a valid UUID.");
            Assert.Equal(PlayerId, wrapper.Data.PlayerId);
            return wrapper.Data.RoomId;
        }

        public static async Task<string> CreateRoomAsync(string playerName, string playerId, HttpClient client)
        {
            var resp = await client.PostAsJsonAsync("/api/room/create", new
            {
                playerName,
                stepsPerRound = new[] { 1 }
            });
            resp.EnsureSuccessStatusCode();
            //var raw = await resp.Content.ReadAsStringAsync();
            //_output.WriteLine(raw);
            var wrapper = await resp.Content.ReadFromJsonAsync<ApiSuccess<CreateRoomDto>>(Json);
            Assert.NotNull(wrapper);
            Assert.NotNull(wrapper.Data);
            Assert.NotNull(wrapper.Data.RoomId);
            Assert.True(Guid.TryParse(wrapper.Data.RoomId, out _), "RoomId must be a valid UUID.");
            Assert.Equal(playerId, wrapper.Data.PlayerId);
            return wrapper.Data.RoomId;
        }

        public async Task JoinRoomAsync(string roomId)
        {
            var resp = await Client.PostAsJsonAsync($"/api/room/{roomId}/join", new
            {
                playerName = PlayerName
            });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine(raw);
            var wrapper = await resp.Content.ReadFromJsonAsync<ApiSuccess<CreateRoomDto>>(Json);
            Assert.NotNull(wrapper);
            Assert.NotNull(wrapper.Data);
            Assert.Equal(roomId, wrapper.Data.RoomId);
            Assert.Equal(PlayerId, wrapper.Data.PlayerId);
            Assert.Equal(PlayerName, wrapper.Data.PlayerName);
        }

        public static async Task JoinRoomAsync(string roomId, string playerName, string playerId, HttpClient client)
        {
            var resp = await client.PostAsJsonAsync($"/api/room/{roomId}/join", new
            {
                playerName
            });
            resp.EnsureSuccessStatusCode();
            //var raw = await resp.Content.ReadAsStringAsync();
            //_output.WriteLine(raw);
            var wrapper = await resp.Content.ReadFromJsonAsync<ApiSuccess<CreateRoomDto>>(Json);
            Assert.NotNull(wrapper);
            Assert.NotNull(wrapper.Data);
            Assert.Equal(roomId, wrapper.Data.RoomId);
            Assert.Equal(playerId, wrapper.Data.PlayerId);
            Assert.Equal(playerName, wrapper.Data.PlayerName);
            Assert.Contains("Joined room successfully", wrapper.Message, StringComparison.OrdinalIgnoreCase);
        }
        // Enhanced room creation with settings
        public async Task<string> CreateRoomWithSettingsAsync(string roomName, int maxPlayers, bool isPrivate, int[] stepsPerRound)
        {
            var resp = await Client.PostAsJsonAsync("/api/room/create", new
            {
                playerName = PlayerName,
                roomName,
                maxPlayers,
                isPrivate,
                stepsPerRound
            });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine($"Create room response: {raw}");
            var wrapper = await resp.Content.ReadFromJsonAsync<ApiSuccess<CreateRoomDto>>(Json);
            Assert.NotNull(wrapper);
            Assert.NotNull(wrapper.Data);
            Assert.NotNull(wrapper.Data.RoomId);
            Assert.True(Guid.TryParse(wrapper.Data.RoomId, out _), "RoomId must be a valid UUID.");
            Assert.Equal(PlayerId, wrapper.Data.PlayerId);
            return wrapper.Data.RoomId;
        }

        // Enhanced room creation with settings and custom map
        public async Task<string> CreateRoomWithCustomMapAsync(string roomName, int maxPlayers, bool isPrivate, int[] stepsPerRound, CustomMapRequest? customMap = null)
        {
            var resp = await Client.PostAsJsonAsync("/api/room/create", new
            {
                playerName = PlayerName,
                roomName,
                maxPlayers,
                isPrivate,
                stepsPerRound,
                customMap
            });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine($"Create room response: {raw}");
            var wrapper = await resp.Content.ReadFromJsonAsync<ApiSuccess<CreateRoomDto>>(Json);
            Assert.NotNull(wrapper);
            Assert.NotNull(wrapper.Data);
            Assert.NotNull(wrapper.Data.RoomId);
            Assert.True(Guid.TryParse(wrapper.Data.RoomId, out _), "RoomId must be a valid UUID.");
            Assert.Equal(PlayerId, wrapper.Data.PlayerId);
            return wrapper.Data.RoomId;
        }

        // Room status
        public async Task<RoomStatusSnapshot> GetRoomStatusAsync(string roomId)
        {
            var resp = await Client.GetAsync($"/api/room/{roomId}/status");
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();

            // Parse the response that's wrapped in ApiSuccess
            var wrapper = await resp.Content.ReadFromJsonAsync<ApiSuccess<RoomStatusSnapshot>>(Json);
            Assert.NotNull(wrapper);
            Assert.NotNull(wrapper.Data);

            // If game is finished, show enhanced status
            if (wrapper.Data.Status == "Finished")
            {
                _output.WriteLine($"🏁 [{PlayerName}] GAME FINISHED! Room {roomId} status:");
                _output.WriteLine($"   Final Round: {wrapper.Data.CurrentRound}, Final Step: {wrapper.Data.CurrentStep}");
                _output.WriteLine($"   Total Players: {wrapper.Data.Racers.Count}");
            }
            else
            {
                _output.WriteLine($"Room status [{PlayerName}]: {wrapper.Data.Status} - Phase: {wrapper.Data.Phase} - R{wrapper.Data.CurrentRound}S{wrapper.Data.CurrentStep}");
            }

            return wrapper.Data;
        }

        // Ready up
        public async Task SetReadyAsync(string roomId, bool isReady)
        {
            var resp = await Client.PostAsJsonAsync($"/api/room/{roomId}/ready", new { isReady });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine($"Ready response [{PlayerName}]: {raw}");
        }

        // Start game
        public async Task StartGameAsync(string roomId)
        {
            var resp = await Client.PostAsJsonAsync($"/api/room/{roomId}/start", new { });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine($"Start game response: {raw}");
        }

        // Get hand
        public async Task<HandResponse> GetHandAsync(string roomId)
        {
            var resp = await Client.GetAsync($"/api/room/{roomId}/hand");
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            var handResp = await resp.Content.ReadFromJsonAsync<ApiSuccess<HandResponse>>(Json);
            Assert.NotNull(handResp);
            Assert.NotNull(handResp.Data);
            return handResp.Data;
        }

        // Draw skip
        public async Task DrawSkipAsync(string roomId)
        {
            var resp = await Client.PostAsJsonAsync($"/api/room/{roomId}/drawSkip", new { });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine($"Draw skip response [{PlayerName}]: {raw}");
        }

        // Submit step card
        public async Task SubmitStepCardAsync(string roomId, string cardId)
        {
            var resp = await Client.PostAsJsonAsync($"/api/room/{roomId}/submit-step-card", new { cardId });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine($"Submit step card response [{PlayerName}]: {raw}");
        }

        // Submit execution parameter
        public async Task SubmitExecParamAsync(string roomId, object execParameter)
        {
            var requestBody = new { ExecParameter = execParameter };
            _output.WriteLine($"[{PlayerName}] Submitting exec param: {JsonSerializer.Serialize(requestBody, Json)}");

            var resp = await Client.PostAsJsonAsync($"/api/room/{roomId}/submit-exec-param", requestBody);

            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _output.WriteLine($"[{PlayerName}] Submit exec param FAILED - Status: {resp.StatusCode}, Response: {raw}");
                _output.WriteLine($"[{PlayerName}] Request was: POST /api/room/{roomId}/submit-exec-param");
                _output.WriteLine($"[{PlayerName}] Request body: {JsonSerializer.Serialize(requestBody, Json)}");
                throw new HttpRequestException($"Submit exec param failed with status {resp.StatusCode}: {raw}");
            }

            _output.WriteLine($"[{PlayerName}] Submit exec param SUCCESS: {raw}");
        }

        // Submit execution parameter with proper validation based on card type
        public async Task SubmitExecParamForCardAsync(string roomId, string cardType)
        {
            object execParam;

            switch (cardType.ToLower())
            {
                case "move":
                    // Move cards: Effect must be 1 or 2
                    execParam = new { Effect = new Random().Next(1, 3), DiscardedCardIds = new List<string>() }; // 1 or 2
                    break;

                case "changelane":
                    // ChangeLane cards: Effect must be 1 or -1
                    execParam = new { Effect = new Random().NextDouble() < 0.5 ? 1 : -1, DiscardedCardIds = new List<string>() };
                    break;

                case "shiftgear":
                    // ShiftGear cards: Effect must be 1 (shift up) or -1 (shift down)
                    execParam = new { Effect = new Random().NextDouble() < 0.5 ? 1 : -1, DiscardedCardIds = new List<string>() };
                    break;

                case "repair":
                    // Repair cards: Must have at least one card to discard
                    var hand = await GetHandAsync(roomId);
                    var cardsToDiscard = hand.Cards.Take(1).Select(c => c.Id).ToList(); // Discard 1 card
                    execParam = new { Effect = -1, DiscardedCardIds = cardsToDiscard };
                    break;

                default:
                    // Fallback to a safe default
                    execParam = new { Effect = 1, DiscardedCardIds = new List<string>() };
                    break;
            }

            _output.WriteLine($"[{PlayerName}] Submitting exec param for {cardType}: Effect={((dynamic)execParam).Effect}, DiscardCount={((dynamic)execParam).DiscardedCardIds.Count}");
            await SubmitExecParamAsync(roomId, execParam);
        }

        // Discard cards
        public async Task DiscardCardsAsync(string roomId, List<string> cardIds)
        {
            var resp = await Client.PostAsJsonAsync($"/api/room/{roomId}/discard-cards", new { cardIds });
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            _output.WriteLine($"Discard cards response [{PlayerName}]: {raw}");
        }
    }
}
