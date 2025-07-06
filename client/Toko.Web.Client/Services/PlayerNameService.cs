using Microsoft.JSInterop;

namespace Toko.Web.Client.Services
{
    public interface IPlayerNameService
    {
        Task<string> GetPlayerNameAsync();
        Task SetPlayerNameAsync(string playerName);
        Task<bool> HasCustomPlayerNameAsync();
    }

    public class PlayerNameService : IPlayerNameService
    {
        private readonly IJSRuntime _jsRuntime;
        private const string STORAGE_KEY = "toko_player_name";
        private string? _cachedPlayerName;

        public PlayerNameService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task<string> GetPlayerNameAsync()
        {
            if (_cachedPlayerName != null)
                return _cachedPlayerName;

            try
            {
                var storedName = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", STORAGE_KEY);
                if (!string.IsNullOrWhiteSpace(storedName))
                {
                    _cachedPlayerName = storedName;
                    return storedName;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading player name from localStorage: {ex.Message}");
            }

            // Generate default name if no stored name exists
            var defaultName = $"Driver-{Random.Shared.Next(1000, 9999)}";
            _cachedPlayerName = defaultName;
            return defaultName;
        }

        public async Task SetPlayerNameAsync(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return;

            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", STORAGE_KEY, playerName.Trim());
                _cachedPlayerName = playerName.Trim();
                Console.WriteLine($"Player name saved: {_cachedPlayerName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving player name to localStorage: {ex.Message}");
            }
        }

        public async Task<bool> HasCustomPlayerNameAsync()
        {
            try
            {
                var storedName = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", STORAGE_KEY);
                return !string.IsNullOrWhiteSpace(storedName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking custom player name: {ex.Message}");
                return false;
            }
        }
    }
}