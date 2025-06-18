namespace Toko.Models.Events
{
    public record HostChanged(string RoomId, string NewHostId, string NewHostName) : IRoomEvent;
}
