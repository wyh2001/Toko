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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();

//builder.Services
//    .AddControllers(options =>
//    {
//        options.Filters.Add<HttpResponseExceptionFilter>();
//    });


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
                "http://localhost:4000",
                "http://127.0.0.1:4000"
            )
            .AllowAnyHeader()                        // 允许所有请求头
            .AllowAnyMethod()                        // 允许 GET、POST、OPTIONS…
            .AllowCredentials();                     // 如果你用 SignalR，需要允许凭据
    });
});




builder.Services.AddSignalR();


//builder.Services.AddRazorPages();             
//builder.Services.AddServerSideBlazor();
//builder.Services.AddMemoryCache();

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
                   // SignalR 在 WebSocket 握手时无法带 Cookie，我们允许 query string 携带
                   var accessToken = context.Request.Query["access_token"];
                   var path = context.HttpContext.Request.Path;
                   if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/racehub"))
                       context.Token = accessToken;
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
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();



app.MapControllers();

app.MapHub<RaceHub>("/raceHub");

//app.MapBlazorHub();
//app.MapFallbackToPage("/_Host");

app.Run();

public partial class Program { }
