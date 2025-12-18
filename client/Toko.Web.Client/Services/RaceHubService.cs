using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Toko.Web.Client.Services;

public record GameEvent(string EventName, object EventData);

public interface IRaceHubService : IAsyncDisposable
{
    event Action<GameEvent>? GameEventReceived;
    event Action<bool>? ConnectionStateChanged;
    event Action<bool>? ReconnectingStateChanged;
    Task ConnectAsync(string hubUrl);
    Task JoinRoomAsync(string roomId);
    Task LeaveRoomAsync(string roomId);
}

public sealed class RaceHubService : IRaceHubService
{
    private HubConnection? _hubConnection;

    public event Action<GameEvent>? GameEventReceived;
    public event Action<bool>? ConnectionStateChanged;
    public event Action<bool>? ReconnectingStateChanged;

    public async Task ConnectAsync(string hubUrl)
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.Closed += (error) =>
        {
            ConnectionStateChanged?.Invoke(false);
            ReconnectingStateChanged?.Invoke(false);
            Console.WriteLine($"SignalR connection closed: {error?.Message}");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnecting += (error) =>
        {
            ConnectionStateChanged?.Invoke(false);
            ReconnectingStateChanged?.Invoke(true);
            Console.WriteLine($"SignalR reconnecting: {error?.Message}");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += (connectionId) =>
        {
            ConnectionStateChanged?.Invoke(true);
            ReconnectingStateChanged?.Invoke(false);
            Console.WriteLine($"SignalR reconnected: {connectionId}");
            return Task.CompletedTask;
        };

        _hubConnection.On<string, object>("OnRoomEvent", (eventName, eventData) =>
        {
            GameEventReceived?.Invoke(new GameEvent(eventName, eventData));
        });

        await _hubConnection.StartAsync();
        ConnectionStateChanged?.Invoke(true);
    }

    public async Task JoinRoomAsync(string roomId)
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("JoinRoom", roomId);
        }
    }

    public async Task LeaveRoomAsync(string roomId)
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("LeaveRoom", roomId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }

        GameEventReceived = null;
        ConnectionStateChanged = null;
        ReconnectingStateChanged = null;
    }
}
