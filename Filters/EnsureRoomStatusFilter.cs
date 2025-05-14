using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Toko.Models;
using Toko.Services;
using static Toko.Models.Room;

namespace Toko.Filters
{
    public class EnsureRoomStatusFilter : IAsyncActionFilter
    {
        private readonly RoomStatus _required;
        private readonly RoomManager _rooms;

        public EnsureRoomStatusFilter(RoomStatus required, RoomManager rooms)
        {
            _required = required;
            _rooms = rooms;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            // 1. 从参数里拿 roomId
            string? roomId = null;
            if (context.ActionArguments.TryGetValue("roomId", out var idObj)
                && idObj is string rid)
            {
                roomId = rid;
            }
            else
            {
                // 如果是 req DTO，尝试找 RoomId 属性
                foreach (var arg in context.ActionArguments.Values)
                {
                    var prop = arg?.GetType().GetProperty("RoomId");
                    if (prop != null)
                    {
                        roomId = prop.GetValue(arg) as string;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(roomId))
            {
                context.Result = new BadRequestObjectResult("缺少 roomId 参数。");
                return;
            }

            // 2. 获取房间，检查状态
            var room = _rooms.GetRoom(roomId);
            if (room == null)
            {
                context.Result = new NotFoundObjectResult("房间不存在。");
                return;
            }
            if (room.Status != _required)
            {
                context.Result = new BadRequestObjectResult(
                    $"此接口只允许 Status = {_required} 的房间调用。当前为 {room.Status}");
                return;
            }

            await next();
        }
    }
}
