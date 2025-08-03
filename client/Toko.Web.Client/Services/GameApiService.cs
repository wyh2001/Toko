using System.Net.Http.Json;
using Toko.Shared.Models;

namespace Toko.Web.Client.Services;

public interface IGameApiService
{
    Task<RoomStatusSnapshot?> GetRoomStatusAsync(string roomId);
    Task<List<CardDto>> GetHandAsync(string roomId);
    Task<bool> PlayCardAsync(string roomId, string cardId);
    Task<bool> DrawCardsAsync(string roomId);
    Task<bool> SubmitParametersAsync(string roomId, object parameters);
    Task<bool> SubmitDiscardCardsAsync(string roomId, List<string> cardIds);
    Task<bool> SkipDiscardAsync(string roomId);
    Task<bool> LeaveGameAsync(string roomId);
}

public sealed class GameApiService : IGameApiService
{
    private readonly HttpClient _http;

    public GameApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<RoomStatusSnapshot?> GetRoomStatusAsync(string roomId)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<ApiSuccess<RoomStatusSnapshot>>($"/api/room/{roomId}");
            return response?.Data;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<CardDto>> GetHandAsync(string roomId)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<ApiSuccess<GetHandDto>>($"/api/room/{roomId}/hand");
            return response?.Data?.Cards ?? new List<CardDto>();
        }
        catch
        {
            return new List<CardDto>();
        }
    }

    public async Task<bool> PlayCardAsync(string roomId, string cardId)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/api/room/{roomId}/submit-step-card", new { cardId });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DrawCardsAsync(string roomId)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/api/room/{roomId}/drawSkip", new { });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SubmitParametersAsync(string roomId, object parameters)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/api/room/{roomId}/submit-exec-param", parameters);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SubmitDiscardCardsAsync(string roomId, List<string> cardIds)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/api/room/{roomId}/discard-cards", new { CardIds = cardIds });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SkipDiscardAsync(string roomId)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/api/room/{roomId}/discard-cards", new { CardIds = new List<string>() });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> LeaveGameAsync(string roomId)
    {
        try
        {
            var response = await _http.PostAsync($"/api/room/{roomId}/leave", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
