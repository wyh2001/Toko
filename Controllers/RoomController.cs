using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using Toko.Filters;
using Toko.Hubs;
using Toko.Models;
using Toko.Models.Requests;
using Toko.Services;
using static Toko.Models.Room;
using static Toko.Services.RoomManager;

namespace Toko.Controllers
{
    [Authorize]
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
        [Idempotent]
        public IActionResult CreateRoom([FromBody] CreateRoomRequest req)
        {
            var playerId = GetPlayerId();
            var (roomId, hostRacer) = _roomManager.CreateRoom(
                playerId, req.RoomName, req.MaxPlayers, req.IsPrivate, req.PlayerName, req.TotalRounds, req.StepsPerRound);

            return Ok(new
            {
                roomId,
                playerId = hostRacer.Id,
                displayName = hostRacer.PlayerName,
            });
        }

        [HttpPost("join")]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        [Idempotent]
        public async Task<IActionResult> JoinRoom([FromBody] JoinRoomRequest request)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.JoinRoom(request.RoomId, playerId, request.PlayerName);
            return result.Match<IActionResult>(
                success => Ok(new { message = "Joined room", playerId = success.Racer.Id }),
                error => error switch
                {
                    JoinRoomError.RoomNotFound => NotFound("Room not found."),
                    JoinRoomError.RoomFull => BadRequest("Room is full."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
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
                    room.CurrentRound
                }
                );
        }

        [AllowAnonymous]
        [HttpGet("list")]
        public IActionResult ListRooms()
        {
            var rooms = _roomManager.GetAllRooms();
            return Ok(rooms.Select(r => new
            {
                r.Id,
                r.Name,
                r.MaxPlayers,
                r.IsPrivate,
                Racers = r.Racers.Select(r => new
                {
                    r.Id,
                    r.PlayerName
                }).ToList(),
                //Map = r.Map?.ToString(),
                r.CurrentRound
            }));
        }

        //[HttpPost("draw")]
        //[EnsureRoomStatus(RoomStatus.Playing)]
        //public IActionResult Draw([FromBody] DrawRequest req)
        //{
        //    var cards = _roomManager.DrawCards(
        //        req.RoomId, req.PlayerId, req.Count);
        //    return cards.Match<IActionResult>(
        //        success => Ok(new { cards = success }),
        //        error => error switch
        //        {
        //            DrawCardsError.RoomNotFound => NotFound("Room not found."),
        //            DrawCardsError.PlayerNotFound => NotFound("Player not found."),
        //            _ => StatusCode(StatusCodes.Status500InternalServerError)
        //        });
        //    //return Ok(cards);
        //}

        /// <summary>
        /// 房主点击“开始游戏”后调用
        /// </summary>
        [HttpPost("{roomId}/start")]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        [Idempotent]
        public async Task<IActionResult> Start(string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.StartRoom(roomId, playerId);
            return result.Match<IActionResult>(success =>
            {
                //_hubContext.Clients.Group(roomId)
                //    .SendAsync("GameStarted", success.RoomId);
                return Ok(new { message = "Game started", success.RoomId });
            },
                error => error switch
                {
                    StartRoomError.RoomNotFound => NotFound("Room not found."),
                    StartRoomError.AlreadyStarted => BadRequest("Room already started."),
                    StartRoomError.AlreadyFinished => BadRequest("Room already Finished"),
                    StartRoomError.NotHost => BadRequest("You are not the host."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        [HttpPost("submit-step-card")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        [Idempotent]
        public async Task<IActionResult> SubmitStepCard([FromBody] SubmitStepCardRequest req)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.SubmitStepCard(req.RoomId, playerId, req.CardId);
            return result.Match<IActionResult>(
                success =>
                {
                    //_hubContext.Clients.Group(req.RoomId)
                    //    .SendAsync("ReceiveStepCardSubmissions", success.CardId);
                    return Ok(new { message = "Step card submitted successfully", success.CardId });
                },
                error => error switch
                {
                    SubmitStepCardError.RoomNotFound => NotFound("Room not found."),
                    SubmitStepCardError.PlayerNotFound => NotFound("Player not found."),
                    SubmitStepCardError.NotYourTurn => BadRequest("Not your step."),
                    SubmitStepCardError.CardNotFound => BadRequest("Invalid card ID."),
                    SubmitStepCardError.WrongPhase => BadRequest("Wrong phase."),
                    SubmitStepCardError.PlayerBanned => BadRequest("Player is banned."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        // 3.2 在执行阶段，提交参数并立刻执行
        [HttpPost("submit-exec-param")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        [Idempotent]
        public async Task<IActionResult> SubmitExecParam([FromBody] SubmitExecParamRequest req)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.SubmitExecutionParam(req.RoomId, playerId, req.ExecParameter);
            return result.Match<IActionResult>(
                success =>
                {
                    return Ok(new { message = "Execution submitted", success });
                },
                error => error switch
                {
                    SubmitExecutionParamError.RoomNotFound => NotFound("Room not found."),
                    SubmitExecutionParamError.PlayerNotFound => NotFound("Player not found."),
                    SubmitExecutionParamError.NotYourTurn => NotFound("Step not found"),
                    SubmitExecutionParamError.CardNotFound => NotFound("Card not found"),
                    SubmitExecutionParamError.InvalidExecParameter => BadRequest("Invalid execution parameter"),
                    SubmitExecutionParamError.PlayerBanned => BadRequest("Player is banned."),
                    SubmitExecutionParamError.WrongPhase => BadRequest("Wrong phase."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        [HttpPost("leave")]
        [Idempotent]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        public async Task<IActionResult> LeaveRoom([FromBody] LeaveRoomRequest req)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.LeaveRoom(req.RoomId, playerId);

            return result.Match<IActionResult>(
                success => Ok(new { message = "Left room successfully.", success.PlayerId }),
                error => error switch
                {
                    LeaveRoomError.RoomNotFound => NotFound("Room not found."),
                    LeaveRoomError.PlayerNotFound => NotFound("Player not found."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }


        [HttpPost("discard-cards")]
        [Idempotent]
        [EnsureRoomStatus(RoomStatus.Playing)]
        public async Task<IActionResult> Discard([FromBody] DiscardRequest req)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.DiscardCards(
                req.RoomId, playerId, req.CardIds);
            return result.Match<IActionResult>(
                success => Ok(new { message = "Discarded cards", success }),
                error => error switch
                {
                    DiscardCardsError.RoomNotFound => NotFound("Room not found."),
                    DiscardCardsError.PlayerNotFound => NotFound("Player not found."),
                    DiscardCardsError.NotYourTurn => NotFound("Step not found."),
                    DiscardCardsError.CardNotFound => BadRequest("Invalid card IDs."),
                    DiscardCardsError.WrongPhase => BadRequest("Wrong phase."),
                    DiscardCardsError.PlayerBanned => BadRequest("Player is banned."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        private string GetPlayerId()
        {
            var playerId = User.FindFirst("PlayerId")?.Value;
            if (string.IsNullOrEmpty(playerId))
            {
                throw new InvalidOperationException("PlayerId not found in JWT token.");
            }
            return playerId;
        }
    }
}
