using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using System.Text;
using Toko.Filters;
using Toko.Hubs;
using Toko.Models;
using Toko.Services;
using Serilog.Settings.Configuration;

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
                "http://127.0.0.1:5174"
            )
            .AllowAnyHeader()                        // 允许所有请求头
            .AllowAnyMethod()                        // 允许 GET、POST、OPTIONS…
            .AllowCredentials();                     // 如果你用 SignalR，需要允许凭据
    });
});




builder.Services.AddSignalR();


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
               OnMessageReceived = context =>
               {
                   // 1) 优先从 Cookie
                   var token = context.Request.Cookies["token"];

                   // 2) 如果是 SignalR WebSocket，会把 token 放到 ?access_token=
                   if (string.IsNullOrEmpty(token) &&
                       context.Request.Path.StartsWithSegments("/racehub") &&     // Hub 路径
                       context.Request.Query.TryGetValue("access_token", out var qs))
                   {
                       token = qs.ToString();
                   }

                   context.Token = token;           // 可能是 null；JwtBearer 会自己处理
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



app.MapControllers();

app.MapHub<RaceHub>("/raceHub");

//app.MapBlazorHub();
//app.MapFallbackToPage("/_Host");

app.Run();

public partial class Program { }
