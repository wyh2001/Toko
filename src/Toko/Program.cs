using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using System.Text.Json;
using Toko.Filters;
using Toko.Hubs;
using Toko.Services;
using static Toko.Filters.ApiWrapperFilter;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Host.UseSerilog();
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Services.AddControllers();
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();

builder.Services.AddOutputCache();

builder.Services
    .AddControllers(options =>
    {
        //options.Filters.Add<HttpResponseExceptionFilter>();
        options.Filters.Add<ApiWrapperFilter>();
    });


// register MediatR
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));


//builder.Services.AddScoped<EnsureRoomStatusFilter>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5174",
                "http://127.0.0.1:5174",
                "https://localhost:7253",
                "https://127.0.0.1:7253"
            )
            .AllowAnyHeader()                        // allow all headers
            .AllowAnyMethod()                        // allow all methods (GET, POST, etc.)
            .AllowCredentials();                     // allow credentials (cookies, authorization headers, etc.)
    });
});




builder.Services.AddSignalR();

builder.Services.AddProblemDetails();

//builder.Services.AddRazorPages();             
//builder.Services.AddServerSideBlazor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IdempotencyFilter>();

string? jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey))
{
    throw new InvalidOperationException("JWT Key is not configured in the application settings.");
}
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
       .AddJwtBearer(options =>
       {
           options.TokenValidationParameters = new TokenValidationParameters
           {
               ValidateIssuer = false,
               ValidateAudience = false,
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
                   // (SignalR WebSocket, token in ?access_token=
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

//app.UseHttpsRedirection();
app.UseOutputCache();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();


app.MapControllers();

app.MapHub<RaceHub>("/raceHub");

//app.MapBlazorHub();
//app.MapFallbackToPage("/_Host");

app.Run();

public partial class Program { }
