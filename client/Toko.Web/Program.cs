using Toko.Web.Client.Components.Pages;
using Toko.Web.Components;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    //.AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddReverseProxy()
    .LoadFromMemory(
        new[]
        {
            new RouteConfig
            {
                RouteId  = "apiRoute",
                ClusterId = "apiCluster",
                Match = new RouteMatch { Path = "/api/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId  = "hubRoute",
                ClusterId= "apiCluster",
                Match    = new RouteMatch { Path = "/racehub/{**catch-all}" }
            }
        },
        new[]
        {
            new ClusterConfig
            {
                ClusterId = "apiCluster",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["d1"] = new()
                    { Address = builder.Configuration["ReverseProxy:ApiAddress"]
                    ?? throw new InvalidOperationException("ReverseProxy:ApiAddress configuration is missing.") }
                }
            }
        });

//builder.Services.AddScoped<IPlayerNameService, PlayerNameServerStub>();
//builder.Services.AddScoped<IAuthenticationService, AuthenticationServiceStub>();
//builder.Services.AddHttpClient(); // stub

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

//app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

//app.UseRouting();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapReverseProxy();
app.MapRazorComponents<App>()
    //.AddInteractiveServerRenderMode();
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Home).Assembly);

app.Run();
