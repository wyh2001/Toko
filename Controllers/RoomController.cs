using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Toko.Filters;
using Toko.Hubs;
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
        private readonly IHubContext<RaceHub> _hubContext;

        public RoomController(RoomManager roomManager,
                              IBackgroundTaskQueue taskQueue,
                              IHubContext<RaceHub> hubContext)
        {
            _roomManager = roomManager;
            _taskQueue = taskQueue;
            _hubContext = hubContext;
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
        [EnsureRoomStatus(RoomStatus.Waiting)]
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
            return Ok(
                new
                {
                    room.Id,
                    room.Name,
                    room.MaxPlayers,
                    room.IsPrivate,
                    Racers = room.Racers.Select(r => new
                    {
                        r.Id,
                        r.PlayerName
                    }).ToList(),
                    Map = room.Map?.ToString(),
                    room.CurrentTurn
                }
                );
        }

        [HttpGet("list")]
        public IActionResult ListRooms()
        {
            var rooms = _roomManager.GetAllRooms();
            return Ok(rooms);
        }

        [HttpPost("draw")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        public IActionResult Draw([FromBody] DrawRequest req)
        {
            var cards = _roomManager.DrawCards(
                req.RoomId, req.PlayerId, req.Count);
            return Ok(cards);
        }

        //[HttpPost("submit-cards")]
        //[EnsureRoomStatus(RoomStatus.Playing)]
        //public IActionResult SubmitCards([FromBody] SubmitCardsRequest req)
        //{
        //    var ok = _roomManager.SubmitCards(
        //        req.RoomId, req.PlayerId, req.CardIds);
        //    if (!ok) return BadRequest("Invalid card selection.");
        //    return Ok(new { message = "Submitted." });
        //}

        //[HttpPost("execute")]
        //[EnsureRoomStatus(RoomStatus.Playing)]
        //public async Task<IActionResult> ExecuteTurnAsync([FromBody] string roomId)
        //{
        //    var room = _roomManager.GetRoom(roomId);
        //    if (room == null)
        //        return NotFound("Room not found.");

        //    // 异步排队执行回合
        //    await _taskQueue.QueueBackgroundWorkItemAsync(async ct =>
        //    {
        //        var executor = new TurnExecutor(room.Map!);
        //        executor.ExecuteTurn(room);

        //        // 执行完后，将日志推送到对应房间分组的所有客户端
        //        await _hubContext
        //            .Clients
        //            .Group(roomId)
        //            .SendAsync("ReceiveTurnLogs", executor.Logs, ct);
        //    });

        //    // 立即返回 202，通知客户端“开始执行了”
        //    return Accepted(new { message = "Turn execution queued." });
        //}

        /// <summary>
        /// 房主点击“开始游戏”后调用
        /// </summary>
        [HttpPost("{roomId}/start")]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        public IActionResult Start(string roomId)
        {
            var ok = _roomManager.StartRoom(roomId);
            if (!ok) return BadRequest("房间不存在或已开始。");
            return Ok(new { message = "游戏已开始" });
        }

        [HttpPost("submit-step-card")]
        public IActionResult SubmitStepCard([FromBody] SubmitStepCardRequest req)
        {
            var list = _roomManager.SubmitStepCard(req.RoomId, req.PlayerId, req.Step, req.CardId);
            // 广播让房间里所有人看见最新卡片提交情况
            _hubContext.Clients.Group(req.RoomId)
                .SendAsync("ReceiveStepCardSubmissions", req.Step, list);
            return Ok(list);
        }

        // 3.2 在执行阶段，提交参数并立刻执行
        [HttpPost("submit-exec-param")]
        public async Task<IActionResult> SubmitExecParam([FromBody] SubmitExecParamRequest req)
        {
            var ins = _roomManager.SubmitExecutionParam(
                req.RoomId, req.PlayerId, req.Step, req.ExecParameter);

            // 获取玩家最新位置等状态
            var room = _roomManager.GetRoom(req.RoomId)!;
            var racer = room.Racers.First(r => r.Id == req.PlayerId);

            // 广播这条执行结果
            await _hubContext.Clients.Group(req.RoomId)
                .SendAsync("ReceiveExecutionResult", req.Step, req.PlayerId, new
                {
                    ins.Type,
                    ins.ExecParameter,
                    racer.SegmentIndex,
                    racer.LaneIndex
                });

            return Ok(ins);
        }

        [HttpPost("leave")]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        public IActionResult LeaveRoom([FromBody] LeaveRoomRequest req)
        {
            var room = _roomManager.GetRoom(req.RoomId);
            if (room == null)
            {
                return NotFound("Room not found.");
            }
            var success = _roomManager.LeaveRoom(req.RoomId, req.PlayerId);
            if (success)
            {
                return Ok(new { message = "Left room successfully." });
            }
            return BadRequest("Failed to leave room.");
        }


        [HttpPost("discard-cards")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        public IActionResult Discard([FromBody] DiscardRequest req)
        {
            var cards = _roomManager.DiscardCards(
                req.RoomId, req.PlayerId, req.Step, req.CardIds);
            return Ok(cards);
        }
    }
}
