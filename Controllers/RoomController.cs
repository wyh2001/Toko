using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Toko.Models;
using Toko.Services;

namespace Toko.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public partial class RoomController : ControllerBase
    {
        private readonly RoomManager _roomManager;
        private readonly IBackgroundTaskQueue _taskQueue;

        public RoomController(RoomManager roomManager, IBackgroundTaskQueue taskQueue)
        {
            _roomManager = roomManager;
            _taskQueue = taskQueue;
        }

        [HttpPost("create")]
        public IActionResult CreateRoom([FromBody] CreateRoomRequest req)
        {
            var (roomId, hostRacer) = _roomManager.CreateRoom(
                req.RoomName, req.MaxPlayers, req.IsPrivate, req.PlayerName
            );

            return Ok(new
            {
                roomId,
                playerId = hostRacer.Id,
                displayName = hostRacer.PlayerName
            });
        }

        [HttpPost("join")]
        public IActionResult JoinRoom([FromBody] JoinRoomRequest request)
        {
            var room = _roomManager.GetRoom(request.RoomId);
            if (room == null)
            {
                return NotFound("Room not found.");
            }

            var (success, racer) = _roomManager.JoinRoom(request.RoomId, request.PlayerName);
            if (success)
            {
                return Ok(new { message = "Joined room successfully.", playerId = racer.Id });
            }

            return BadRequest("Failed to join room.");
        }

        [HttpGet("{roomId}")]
        public IActionResult GetRoom(string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                return NotFound("Room not found.");
            }
            return Ok(room);
        }

        [HttpGet("list")]
        public IActionResult ListRooms()
        {
            var rooms = _roomManager.GetAllRooms();
            return Ok(rooms);
        }

        [HttpPost("submit")]
        public IActionResult SubmitInstructions([FromBody] SubmitInstructionsRequest request)
        {
            var room = _roomManager.GetRoom(request.RoomId);
            if (room == null)
            {
                return NotFound("Room not found.");
            }

            if (_roomManager.SubmitInstructions(request.RoomId, request.PlayerId, request.Instructions))
            {
                return Ok(new { message = "Instructions submitted successfully." });
            }

            return BadRequest("Failed to submit instructions.");
        }

        [HttpPost("execute")]
        public async Task<IActionResult> ExecuteTurn([FromBody] string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return NotFound("Room not found.");

            // 排队执行
            await _taskQueue.QueueBackgroundWorkItemAsync(async ct =>
            {
                var executor = new TurnExecutor();
                executor.ExecuteTurn(room);
                await Task.CompletedTask;
            });

            // 202 Accepted 表示已接受，后台正在执行
            return Accepted(new { message = "Turn execution queued." });
        }
    }
}
