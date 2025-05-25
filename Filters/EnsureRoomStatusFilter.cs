using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Toko.Models.Requests;
using Toko.Services;
using static Toko.Models.Room;

namespace Toko.Filters
{
    /// <summary>
    /// Ensures the Room with the given RoomId is in the required status before executing the action.
    /// </summary>
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
            // Try to get any argument implementing IRoomRequest
            var roomRequest = context
                .ActionArguments
                .Values
                .OfType<IRoomRequest>()
                .FirstOrDefault();

            // Validate presence of RoomId
            if (roomRequest == null || string.IsNullOrWhiteSpace(roomRequest.RoomId))
            {
                context.Result = new BadRequestObjectResult("Missing or empty roomId.");
                return;
            }

            var roomId = roomRequest.RoomId;

            // Look up the room
            var room = _rooms.GetRoom(roomId);
            if (room == null)
            {
                context.Result = new NotFoundObjectResult("Room not found.");
                return;
            }

            // Check that its status matches the required one
            if (room.Status != _required)
            {
                context.Result = new BadRequestObjectResult(
                    $"This endpoint requires room status = {_required}, but current status is {room.Status}.");
                return;
            }

            // All good: proceed to the action
            await next();
        }
    }
}
