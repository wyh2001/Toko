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
    public class EnsureRoomStatusFilter(Models.Room.RoomStatus required, RoomManager rooms) : IAsyncActionFilter
    {
        private readonly RoomStatus _required = required;
        private readonly RoomManager _rooms = rooms;

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

            var roomId = roomRequest?.RoomId;
            // If not found, try to get it from the route data and action arguments
            roomId ??= context.RouteData.Values.TryGetValue("roomId", out var routeVal) ? routeVal?.ToString() : null;
            roomId ??= context.ActionArguments.TryGetValue("roomId", out var arg) ? arg as string : null;

            // Validate presence of RoomId
            if (string.IsNullOrWhiteSpace(roomId))
            {
                context.Result = new BadRequestObjectResult("Missing or empty roomId.");
                return;
            }

            // Validate if uuid format
            if (!Guid.TryParse(roomId, out _))
            {
                context.Result = new BadRequestObjectResult("Invalid roomId format. Must be a valid UUID.");
                return;
            }

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
