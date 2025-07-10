using Toko.Web.Client.Services;

public class AuthenticationServiceStub : IAuthenticationService
{
    public bool IsAuthenticated => true;
    public string? PlayerId => "stub-player-id";
    public Task<string> GetPlayerNameAsync() => Task.FromResult("StubPlayer");
    public void ClearAuthentication()
    {
        // No-op for stub
    }
    public Task<bool> EnsureAuthenticatedAsync()
    {
        // Always return true for stub
        return Task.FromResult(true);
    }

}