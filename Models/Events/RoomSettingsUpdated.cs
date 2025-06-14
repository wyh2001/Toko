using Toko.Services;

namespace Toko.Models.Events
{
    public class RoomSettingsUpdated(string roomId, RoomManager.RoomSettings settings) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public RoomManager.RoomSettings Settings { get; } = settings;
    }
}