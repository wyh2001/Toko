using Toko.Filters;
using Toko.Hubs;
using Toko.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

//app.UseAuthorization();

app.UseCors();

app.MapControllers();

app.MapHub<RaceHub>("/raceHub");

app.Run();
