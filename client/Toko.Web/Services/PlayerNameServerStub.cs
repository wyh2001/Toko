using Toko.Web.Client.Services;

namespace Toko.Web.Services
{
    public class PlayerNameServerStub : IPlayerNameService
    {
        public Task<string> GetPlayerNameAsync() => Task.FromResult("ServerRender");
        public Task SetPlayerNameAsync(string _) => Task.CompletedTask;
        public Task<bool> HasCustomPlayerNameAsync() => Task.FromResult(false);
    }
}
