using Toko.Services;

namespace Toko.Models.Events
{
    public record RoomSettingsUpdated(string RoomId, RoomManager.RoomSettings Settings) : IRoomEvent;
}