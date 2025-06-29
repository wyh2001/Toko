using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace Toko.Hubs
{
    public class RaceHub : Hub
    {
        /// <summary>
        /// 客户端调用，加入到房间分组，便于接收该房间的推送
        /// </summary>
        public Task JoinRoom(string roomId)
            => Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        /// <summary>
        /// 客户端离开房间分组
        /// </summary>
        public Task LeaveRoom(string roomId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
    }
}
