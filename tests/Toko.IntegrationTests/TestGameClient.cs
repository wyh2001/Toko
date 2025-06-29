using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit.Abstractions;
using static Toko.Controllers.RoomController;

namespace Toko.IntegrationTests
{
    public sealed class TestGameClient(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public HttpClient Client { get; } = factory.CreateClient();
        public string PlayerId { get; private set; } = "";
        public string PlayerName { get; set; } = "";
        private readonly ITestOutputHelper _output = output;

        //private record ApiSuccess<T>(string Message, T Data);
        //private record ApiError(object Error);
        private record CreateRoomDto(string RoomId, string PlayerId, string PlayerName);
        private record JoinRommDto(string RoomId, string PlayerId, string PlayerName);
        private record AuthDto(string PlayerName, string PlayerId);

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
    }
}
