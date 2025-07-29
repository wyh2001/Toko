using Toko.Web.Client.Services;

namespace Toko.Web.Services
{
    public class PlayerNameServerStub : IPlayerNameService
    {
        public Task<string> GetPlayerNameAsync() => Task.FromResult("ServerRender");
        public Task<(bool Success, string? ErrorMessage)> SetPlayerNameAsync(string _) => Task.FromResult((true, (string?)null));
        public Task<bool> HasCustomPlayerNameAsync() => Task.FromResult(false);
    }
}
