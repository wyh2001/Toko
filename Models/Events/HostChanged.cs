namespace Toko.Models.Events
{
    public class HostChanged : IRoomEvent
    {
        public string RoomId { get; }
        public string NewHostId { get; }
        public string NewHostName { get; }

        public HostChanged(string roomId, string newHostId, string newHostName)
        {
            RoomId = roomId;
            NewHostId = newHostId;
            NewHostName = newHostName;
        }
    }
}
