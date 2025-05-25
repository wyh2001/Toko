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
    public partial class RoomController : ControllerBase
    {
        private readonly RoomManager _roomManager;
        //private readonly IBackgroundTaskQueue _taskQueue;
        //private readonly IHubContext<RaceHub> _hubContext;

        public record ApiSuccess<T>(string message, T? data);
        public record ApiError(string error);

        public RoomController(RoomManager roomManager)
        {
            _roomManager = roomManager;
            //_taskQueue = taskQueue;
            //_hubContext = hubContext;
        }

        [HttpPost("create")]
        [Idempotent]
        public IActionResult CreateRoom([FromBody] CreateRoomRequest req)
        {
            if (req.TotalRounds != req.StepsPerRound.Count)
            {
                ModelState.AddModelError(nameof(req.StepsPerRound), "StepsPerRound count must match TotalRounds.");
                return BadRequest(ModelState);
            }
            
            var playerId = GetPlayerId();
            var (roomId, hostRacer) = _roomManager.CreateRoom(
                playerId, req.RoomName, req.MaxPlayers, req.IsPrivate, req.PlayerName, req.TotalRounds, req.StepsPerRound);

            return Ok(new
            {
                message = "Room created successfully",
                data = new
                {
                    roomId,
                    playerId = hostRacer.Id,
                    displayName = hostRacer.PlayerName,
                }
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
                //success => Ok(new { message = "Joined room", playerId = success.Racer.Id }),
                success => Ok(new
                {
                    message = "Joined room successfully",
                    data = new
                    {
                        playerId = success.Racer.Id,
                        displayName = success.Racer.PlayerName,
                        roomId = success.RoomId
                    }
                }),
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
            //return Ok(
            //    new
            //    {
            //        room.Id,
            //        room.Name,
            //        room.MaxPlayers,
            //        room.IsPrivate,
            //        Racers = room.Racers.Select(r => new
            //        {
            //            r.Id,
            //            r.PlayerName
            //        }).ToList(),
            //        //Map = room.Map?.ToString(),
            //        room.CurrentRound
            //    }
            //    );
            return Ok(new
            {
                message = "Room details retrieved successfully",
                data = new
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
                    room.CurrentRound
                }
            });
        }

        [AllowAnonymous]
        [HttpGet("list")]
        [OutputCache(Duration = 5)]
        public IActionResult ListRooms()
        {
            var rooms = _roomManager.GetAllRooms();
            //return Ok(rooms.Select(r => new
            //{
            //    r.Id,
            //    r.Name,
            //    r.MaxPlayers,
            //    r.IsPrivate,
            //    Racers = r.Racers.Select(r => new
            //    {
            //        r.Id,
            //        r.PlayerName
            //    }).ToList(),
            //    r.CurrentRound
            //}));
            return Ok(new
            {
                message = "Room list retrieved successfully",
                data = rooms.Select(r => new
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
                })
            });
        }

        [HttpPost("drawSkip")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        public async Task<IActionResult> DrawSkipAsync([FromBody] DrawSkipRequest req)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.DrawSkip(req.RoomId, playerId);
            return result.Match<IActionResult>(
                //success => Ok(new { message = "Draw skip", success }),
                success => Ok(new
                {
                    message = "Draw skip successful",
                    data = new
                    {
                        success.RoomId,
                        success.PlayerId,
                        success.DrawnCards,
                    }
                }),
                error => error switch
                {
                    DrawSkipError.RoomNotFound => NotFound("Room not found."),
                    DrawSkipError.PlayerNotFound => NotFound("Player not found."),
                    DrawSkipError.NotYourTurn => BadRequest("Not your turn."),
                    DrawSkipError.WrongPhase => BadRequest("Wrong phase."),
                    DrawSkipError.PlayerBanned => BadRequest("Player is banned."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
            //var cards = _roomManager.DrawCards(
            //    req.RoomId, req.PlayerId, req.Count);
            //return cards.Match<IActionResult>(
            //    success => Ok(new { cards = success }),
            //    error => error switch
            //    {
            //        DrawCardsError.RoomNotFound => NotFound("Room not found."),
            //        DrawCardsError.PlayerNotFound => NotFound("Player not found."),
            //        _ => StatusCode(StatusCodes.Status500InternalServerError)
            //    });
            //return Ok(cards);
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
                //return Ok(new { message = "Game started", success.RoomId });
                return Ok(new
                {
                    message = "Game started successfully",
                    data = new
                    {
                        success.RoomId
                    }
                });
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
                    //return Ok(new { message = "Step card submitted successfully", success.CardId });
                    return Ok(new
                    {
                        message = "Step card submitted successfully",
                        data = new
                        {
                            success.RoomId,
                            success.PlayerId,
                            success.CardId
                        }
                    });
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
                    //return Ok(new { message = "Execution submitted", success });
                    return Ok(new
                    {
                        message = "Execution submitted successfully",
                        data = new
                        {
                            success.RoomId,
                            success.PlayerId,
                            success.Instruction,
                        }
                    });
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
                //success => Ok(new { message = "Left room successfully.", success.PlayerId }),
                success => Ok(new
                {
                    message = "Left room successfully",
                    data = new
                    {
                        success.RoomId,
                        success.PlayerId
                    }
                }),
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
                //success => Ok(new { message = "Discarded cards", success }),
                success => Ok(new
                {
                    message = "Cards discarded successfully",
                    data = new
                    {
                        success.RoomId,
                        success.PlayerId,
                        success.cardIds
                    }
                }),
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
        [HttpPost("ready")]
        [Idempotent]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        public async Task<IActionResult> Ready([FromBody] ReadyRequest req)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.ReadyUp(req.RoomId, playerId, req.IsReady);
            return result.Match<IActionResult>(
                //success => Ok(new { message = "Ready", success }),
                success => Ok(new
                {
                    message = "Ready status updated successfully",
                    data = new
                    {
                        success.RoomId,
                        success.PlayerId,
                        success.IsReady
                    }
                }),
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
