using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Toko.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IPlayerNameService, PlayerNameService>();
builder.Services.AddScoped<IRaceHubService, RaceHubService>();
builder.Services.AddScoped<IGameApiService, GameApiService>();

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

await builder.Build().RunAsync();
