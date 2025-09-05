using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace Toko.Hubs
{
    public class RaceHub : Hub
    {
        /// <summary>
        /// Called by client to join a room group to receive pushes for that room
        /// </summary>
        public Task JoinRoom(string roomId)
            => Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        /// <summary>
        /// Called by client to leave a room group
        /// </summary>
        public Task LeaveRoom(string roomId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
    }
}
