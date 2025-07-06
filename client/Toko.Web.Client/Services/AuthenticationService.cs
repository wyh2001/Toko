using System.Net;
using System.Net.Http.Json;
using Toko.Shared.Dtos;

namespace Toko.Web.Client.Services
{
    public interface IAuthenticationService
    {
        Task<bool> EnsureAuthenticatedAsync();
        bool IsAuthenticated { get; }
        string? PlayerId { get; }
        Task<string> GetPlayerNameAsync();
        void ClearAuthentication();
    }

    public class AuthenticationService(HttpClient httpClient, IPlayerNameService playerNameService) : IAuthenticationService
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly IPlayerNameService _playerNameService = playerNameService;
        private bool _isAuthenticated = false;
        private string? _playerId;

        public bool IsAuthenticated => _isAuthenticated;
        public string? PlayerId => _playerId;

        // Get player name from the PlayerNameService
        public async Task<string> GetPlayerNameAsync()
        {
            return await _playerNameService.GetPlayerNameAsync();
        }

        public void ClearAuthentication()
        {
            _isAuthenticated = false;
            _playerId = null;
            Console.WriteLine("Authentication state cleared");
        }

        public async Task<bool> EnsureAuthenticatedAsync()
        {
            if (_isAuthenticated) return true;

            try
            {
                var response = await _httpClient.GetAsync("/api/auth/anon");
                
                if (response.IsSuccessStatusCode)
                {
                    // Both new and existing authentication return JSON with playerId
                    try
                    {
                        var wrapper = await response.Content.ReadFromJsonAsync<ApiSuccess<AuthDto>>();
                        if (wrapper?.Data != null)
                        {
                            _playerId = wrapper.Data.PlayerId;
                            _isAuthenticated = true;
                            
                            // Only set player name if it's provided (new users) and no custom name exists
                            if (!string.IsNullOrEmpty(wrapper.Data.PlayerName) && 
                                !await _playerNameService.HasCustomPlayerNameAsync())
                            {
                                await _playerNameService.SetPlayerNameAsync(wrapper.Data.PlayerName);
                            }
                            
                            Console.WriteLine($"Successfully authenticated with playerId: {_playerId}");
                            return true;
                        }
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        Console.WriteLine($"JSON parsing error on success response: {ex.Message}");
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Response content: {responseContent}");
                        return false;
                    }
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("Authentication failed - unauthorized. Clearing any cached state.");
                    ClearAuthentication();
                    return false;
                }
                else
                {
                    Console.WriteLine($"Authentication failed: {response.StatusCode}");
                    var responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response content: {responseContent}");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Network error during authentication: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Authentication error: {ex.Message}");
            }

            return false;
        }
    }
}