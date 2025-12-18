using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using System.Text.Json;
using Toko.Filters;
using Toko.Hubs;
using Toko.Services;
using Toko.Options;
using static Toko.Filters.ApiWrapperFilter;
using Toko.Infrastructure.Eventing;
using Toko.Handlers;
using Toko.Models.Events;

var builder = WebApplication.CreateBuilder(args);

var trustForwarded = builder.Configuration.GetValue("TRUST_FORWARDED_HEADERS", true);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    if (trustForwarded)
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
    else
    {
        options.ForwardedHeaders = ForwardedHeaders.None;
    }
});

// Add services to the container.
builder.Host.UseSerilog();
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Services.AddControllers();
builder.Services.AddSingleton<RoomManager>();

builder.Services.AddOutputCache();

builder.Services
    .AddControllers(options =>
    {
        //options.Filters.Add<HttpResponseExceptionFilter>();
        options.Filters.Add<ApiWrapperFilter>();
    });


builder.Services.AddSingleton<IEventChannel, DefaultEventChannel>();

builder.Services.AddHostedService<ChannelDispatchService>();
builder.Services.AddTransient<GameEndedHandler>();
builder.Services.AddTransient<RoomAbandonedHandler>();
builder.Services.AddTransient<RoomEventHandler>();
builder.Services.AddTransient<LogEventHandler>();


//builder.Services.AddScoped<EnsureRoomStatusFilter>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSignalR();

builder.Services.AddProblemDetails();

//builder.Services.AddRazorPages();             
//builder.Services.AddServerSideBlazor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IdempotencyFilter>();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration section not found. Please ensure the configuration contains a valid JWT section with required Key property (minimum 32 characters).");

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
       .AddJwtBearer(options =>
       {
           options.TokenValidationParameters = new TokenValidationParameters
           {
               ValidateIssuer = !string.IsNullOrEmpty(jwtOptions.Issuer),
               ValidateAudience = !string.IsNullOrEmpty(jwtOptions.Audience),
               ValidIssuer = jwtOptions.Issuer,
               ValidAudience = jwtOptions.Audience,
               ValidateLifetime = true,
               IssuerSigningKey = signingKey
           };

           options.Events = new JwtBearerEvents
           {
               // 401 Unauthorized
               OnChallenge = context =>
               {
                   context.HandleResponse();

                   var pd = ToApiError(
                       "Unauthorized access.",
                       StatusCodes.Status401Unauthorized,
                       context.HttpContext);

                   context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                   context.Response.ContentType = "application/json";
                   return context.Response.WriteAsync(JsonSerializer.Serialize(pd));
               },

               // 403 Forbidden
               OnForbidden = context =>
               {
                   context.Response.StatusCode = StatusCodes.Status403Forbidden;
                   context.Response.ContentType = "application/json";

                   var pd = ToApiError(
                       "Forbidden access. You do not have permission to access this resource.",
                       StatusCodes.Status403Forbidden,
                       context.HttpContext);

                   return context.Response.WriteAsync(JsonSerializer.Serialize(pd));
               },
               OnMessageReceived = context =>
               {
                   // firstly from cookie
                   var token = context.Request.Cookies["token"];

                   // if not found then from query string
                   // (for fallback purpose only)
                   if (string.IsNullOrEmpty(token) &&
                       context.Request.Path.StartsWithSegments("/racehub") &&     // Hub path
                       context.Request.Query.TryGetValue("access_token", out var qs))
                   {
                       token = qs.ToString();
                   }

                   context.Token = token;           // may be null, leave it to the middleware
                   return Task.CompletedTask;
               }
           };
       });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
//app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();


app.MapControllers();

app.MapHub<RaceHub>("/raceHub");

//app.MapBlazorHub();
//app.MapFallbackToPage("/_Host");

app.Run();

public partial class Program { }
