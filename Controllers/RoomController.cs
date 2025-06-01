using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Toko.Filters;
using Toko.Hubs;
using Toko.Models.Requests;
using Toko.Services;
using static Toko.Controllers.RoomController;
using static Toko.Models.Room;
using static Toko.Services.RoomManager;

namespace Toko.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public partial class RoomController(RoomManager roomManager) : ControllerBase
    {
        private readonly RoomManager _roomManager = roomManager;
        //private readonly IBackgroundTaskQueue _taskQueue;
        //private readonly IHubContext<RaceHub> _hubContext;

        public record ApiSuccess<T>(string Message, T? Data);
        //public record ApiError(string Message, object? Errors = null, string? TraceId = null);

        [HttpPost("create")]
        [Idempotent]
        public IActionResult CreateRoom([FromBody] CreateRoomRequest req)
        {
            var playerId = GetPlayerId();
            var (roomId, hostRacer) = _roomManager.CreateRoom(
                playerId, req.RoomName, req.MaxPlayers, req.IsPrivate, req.PlayerName, req.StepsPerRound);

            return Ok(new ApiSuccess<object>(
                "Room created successfully",
                new
                {
                    roomId,
                    playerId = hostRacer.Id,
                    playerName = hostRacer.PlayerName,
                }
            ));
        }

        [HttpPost("{roomId}/join")]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        [Idempotent]
        public async Task<IActionResult> JoinRoom([FromBody] JoinRoomRequest request, string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.JoinRoom(roomId, playerId, request.PlayerName);
            return result.Match<IActionResult>(
                success => Ok(new ApiSuccess<object>(
                    "Joined room successfully",
                    new
                    {
                        roomId = success.RoomId,
                        playerId = success.Racer.Id,
                        playerName = success.Racer.PlayerName,
                    }
                )),
                error => error switch
                {
                    JoinRoomError.RoomNotFound => NotFound("Room not found."),
                    JoinRoomError.RoomFull => BadRequest("Room is full."),
                    JoinRoomError.AlreadyJoined => BadRequest("Already joined this room."),
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
            return Ok(new ApiSuccess<object>(
                "Room details retrieved successfully",
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
                    //Map = room.Map?.ToString(),
                    //room.CurrentRound
                    Status = room.Status.ToString(),
                }
            ));
        }

        [AllowAnonymous]
        [HttpGet("list")]
        [OutputCache(Duration = 5)]
        public IActionResult ListRooms()
        {
            var rooms = _roomManager.GetAllRooms();
            return Ok(new ApiSuccess<object>(
                "Room list retrieved successfully",
                rooms
                    .Where(r => !r.IsPrivate)
                    .Select(r => new
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
                        //r.CurrentRound
                        //r.Status,
                        Status = r.Status.ToString()
                    })
            ));
        }

        [HttpPost("drawSkip")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        public async Task<IActionResult> DrawSkipAsync([FromBody] DrawSkipRequest req)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.DrawSkip(req.RoomId, playerId);
            return result.Match<IActionResult>(
                success => Ok(new ApiSuccess<object>(
                    "Draw skip successful",
                    new
                    {
                        success.RoomId,
                        success.PlayerId,
                        success.DrawnCards,
                    }
                )),
                error => error switch
                {
                    DrawSkipError.RoomNotFound => NotFound("Room not found."),
                    DrawSkipError.PlayerNotFound => NotFound("Player not found."),
                    DrawSkipError.NotYourTurn => BadRequest("Not your turn."),
                    DrawSkipError.WrongPhase => BadRequest("Wrong phase."),
                    DrawSkipError.PlayerBanned => BadRequest("Player is banned."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        [HttpPost("start")]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        [Idempotent]
        public async Task<IActionResult> Start([FromBody] StartRoomRequest req) 
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.StartRoom(req.RoomId, playerId);
            return result.Match<IActionResult>(success =>
            {
                //_hubContext.Clients.Group(roomId)
                //    .SendAsync("GameStarted", success.RoomId);
                return Ok(new ApiSuccess<object>(
                    "Game started successfully",
                    new
                    {
                        success.RoomId
                    }
                ));
            },
                error => error switch
                {
                    StartRoomError.RoomNotFound => NotFound("Room not found."),
                    StartRoomError.AlreadyStarted => BadRequest("Room already started."),
                    StartRoomError.AlreadyFinished => BadRequest("Room already Finished"),
                    StartRoomError.NotHost => BadRequest("You are not the host."),
                    StartRoomError.NotAllReady => BadRequest("Not all players are ready."),
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
                    return Ok(new ApiSuccess<object>(
                        "Step card submitted successfully",
                        new
                        {
                            success.RoomId,
                            success.PlayerId,
                            success.CardId
                        }
                    ));
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
                    return Ok(new ApiSuccess<object>(
                        "Execution submitted successfully",
                        new
                        {
                            success.RoomId,
                            success.PlayerId,
                            success.Instruction,
                        }
                    ));
                },
                error => error switch
                {
                    SubmitExecutionParamError.RoomNotFound => NotFound("Room not found."),
                    SubmitExecutionParamError.PlayerNotFound => NotFound("Player not found."),
                    SubmitExecutionParamError.NotYourTurn => BadRequest("Not your turn."),
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
                success => Ok(new ApiSuccess<object>(
                    "Left room successfully",
                    new
                    {
                        success.RoomId,
                        success.PlayerId
                    }
                )),
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
                success => Ok(new ApiSuccess<object>(
                    "Cards discarded successfully",
                    new
                    {
                        success.RoomId,
                        success.PlayerId,
                        success.CardIds
                    }
                )),
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

        // ready up
        [HttpPost("{roomId}/ready")]
        [Idempotent]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        public async Task<IActionResult> Ready([FromBody] ReadyRequest req, string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.ReadyUp(roomId, playerId, req.IsReady);
            return result.Match<IActionResult>(
                success => Ok(new ApiSuccess<object>(
                    "Ready status updated successfully",
                    new
                    {
                        success.RoomId,
                        success.PlayerId,
                        success.IsReady
                    }
                )),
                error => error switch
                {
                    ReadyUpError.RoomNotFound => NotFound("Room not found."),
                    ReadyUpError.PlayerNotFound => NotFound("Player not found."),
                    //ReadyUpError.AlreadyReady => BadRequest("Already ready."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        private string GetPlayerId()
        {
            var playerId = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
            {
                throw new InvalidOperationException("PlayerId not found in JWT token.");
            }
            return playerId;
        }
    }
}
