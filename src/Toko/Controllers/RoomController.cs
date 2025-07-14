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
using Toko.Shared.Models;
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
                playerId, req.RoomName, req.MaxPlayers, req.IsPrivate, req.PlayerName, req.StepsPerRound, req.CustomMap);

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
        public async Task<IActionResult> GetRoom(string roomId)
        {
            var result = await _roomManager.GetRoomStatusAsync(roomId);
            return result.Match<IActionResult>(
                success =>
                {
                    return Ok(new ApiSuccess<object>(
                        "Room details retrieved successfully",
                        success.Snapshot
                    ));
                },
                error => error switch
                {
                    RoomManager.GetRoomStatusError.RoomNotFound => NotFound("Room not found."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
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
                        Racers = r.Racers.Select(racer => new
                        {
                            racer.Id,
                            Name = racer.PlayerName,
                        }).ToList(),
                        //Map = r.Map?.ToString(),
                        //r.CurrentRound
                        //r.Status,
                        Status = r.Status.ToString()
                    })
            ));
        }

        [AllowAnonymous]
        [HttpGet("waiting-count")]
        [OutputCache(Duration = 5)]
        public IActionResult GetWaitingRoomsCount()
        {
            var waitingCount = _roomManager.GetWaitingRoomsCount();
            return Ok(new ApiSuccess<object>(
                "Waiting rooms count retrieved successfully",
                new
                {
                    count = waitingCount
                }
            ));
        }

        [AllowAnonymous]
        [HttpGet("completed-count")]
        [OutputCache(Duration = 60)]
        public IActionResult GetNormallyCompletedRoomsCount()
        {
            var completedCount = _roomManager.GetNormallyCompletedRoomsCount();
            return Ok(new ApiSuccess<object>(
                "Normally completed rooms count retrieved successfully",
                new
                {
                    count = completedCount
                }
            ));
        }

        [AllowAnonymous]
        [HttpGet("room-counts")]
        [OutputCache(Duration = 60)]
        public IActionResult GetRoomCounts()
        {
            var waitingCount = _roomManager.GetWaitingRoomsCount();
            var playingCount = _roomManager.GetPlayingRoomsCount();
            var playingRacersCount = _roomManager.GetPlayingRacersCount();
            var finishedCount = _roomManager.GetNormallyCompletedRoomsCount();
            return Ok(new ApiSuccess<object>(
                "Room counts retrieved successfully",
                new
                {
                    waitingCount,
                    playingCount,
                    playingRacersCount,
                    finishedCount
                }
            ));
        }

        [HttpPost("{roomId}/drawSkip")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        public async Task<IActionResult> DrawSkipAsync(string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.DrawSkip(roomId, playerId);
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
                    StartRoomError.NotInTheRoom => NotFound("You are not in the room."),
                    StartRoomError.NotHost => StatusCode(403, "You are not the host"),
                    StartRoomError.NotAllReady => BadRequest("Not all players are ready."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        [HttpPost("{roomId}/submit-step-card")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        [Idempotent]
        public async Task<IActionResult> SubmitStepCard([FromBody] SubmitStepCardRequest req, string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.SubmitStepCard(roomId, playerId, req.CardId);
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
                    SubmitStepCardError.InvalidCardType => BadRequest("Invalid card type submitted."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        [HttpPost("{roomId}/submit-exec-param")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        [Idempotent]
        public async Task<IActionResult> SubmitExecParam([FromBody] SubmitExecParamRequest req, string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.SubmitExecutionParam(roomId, playerId, req.ExecParameter);
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

        [HttpPost("{roomId}/leave")]
        [Idempotent]
        //[EnsureRoomStatus(RoomStatus.Waiting)]
        public async Task<IActionResult> LeaveRoom(string roomId)
        {
            // Ensure the roomId is a valid UUID format
            if (!Guid.TryParse(roomId, out _))
            {
                return BadRequest("Invalid roomId format. Must be a valid UUID.");
            }
            var playerId = GetPlayerId();
            var result = await _roomManager.LeaveRoom(roomId, playerId);

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
                    LeaveRoomError.PlayerNotFound => NotFound("Player not in the room."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }


        [HttpPost("{roomId}/discard-cards")]
        [Idempotent]
        [EnsureRoomStatus(RoomStatus.Playing)]
        public async Task<IActionResult> Discard([FromBody] DiscardRequest req, string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.DiscardCards(
                roomId, playerId, req.CardIds);
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

        [HttpGet("{roomId}/hand")]
        [EnsureRoomStatus(RoomStatus.Playing)]
        public async Task<IActionResult> GetHand(string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.GetHand(roomId, playerId);
            return result.Match<IActionResult>(
                success => Ok(new ApiSuccess<object>(
                    "Hand retrieved successfully",
                    new
                    {
                        success.RoomId,
                        success.PlayerId,
                        Cards = success.Hand.Select(c => new
                        {
                            c.Id,
                            Type = c.Type.ToString(),
                        }).ToList()
                    }
                )),
                error => error switch
                {
                    GetHandError.RoomNotFound => NotFound("Room not found."),
                    GetHandError.PlayerNotFound => NotFound("Player not found."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        [HttpGet("{roomId}/status")]
        public async Task<IActionResult> GetRoomStatus(string roomId)
        {
            var result = await _roomManager.GetRoomStatusAsync(roomId);
            return result.Match<IActionResult>(
                success => Ok(success.Snapshot),
                error => error switch
                {
                    GetRoomStatusError.RoomNotFound => NotFound(new { message = "Room not found." }),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        [HttpPost("{roomId}/kick")]
        public async Task<IActionResult> KickPlayer([FromBody] KickPlayerRequest req, string roomId)
        {
            var playerId = GetPlayerId();
            var result = await _roomManager.KickPlayer(roomId, playerId, req.KickedPlayerId);
            return result.Match<IActionResult>(
                success => Ok(new ApiSuccess<object>(
                    "Player kicked successfully",
                    new
                    {
                        success.RoomId,
                        success.PlayerId,
                        success.KickedPlayerId
                    }
                )),
                error => error switch
                {
                    KickPlayerError.RoomNotFound => NotFound("Room not found."),
                    KickPlayerError.PlayerNotFound => NotFound("Player not found."),
                    KickPlayerError.NotHost => StatusCode(StatusCodes.Status403Forbidden, "You are not the host."),
                    KickPlayerError.TargetNotFound => NotFound("Player to kick not found."),
                    KickPlayerError.TooEarly => BadRequest("Cannot kick player that still has time."),
                    KickPlayerError.WrongPhase => BadRequest("Wrong phase."),
                    KickPlayerError.AlreadyKicked => BadRequest("Player already kicked."),
                    _ => StatusCode(StatusCodes.Status500InternalServerError)
                });
        }

        [HttpPost("{roomId}/updateSettings")]
        [EnsureRoomStatus(RoomStatus.Waiting)]
        public async Task<IActionResult> UpdateRoomSettings([FromBody] UpdateRoomSettingsRequest req, string roomId)
        {
            var playerId = GetPlayerId();
            if (req.IsEmpty)
            {
                return BadRequest("At least one setting must be provided.");
            }
            var settings = new RoomSettings(
                req.RoomName,
                req.MaxPlayers,
                req.IsPrivate,
                req.StepsPerRound
            );

            var result = await _roomManager.UpdateRoomSettings(roomId, playerId, settings);
            return result.Match<IActionResult>(
                success => Ok(new ApiSuccess<object>(
                    "Room settings updated successfully",
                    new
                    {
                        success.RoomId,
                        success.PlayerId,
                        success.Settings
                    }
                )),
                error => error switch
                {
                    UpdateRoomSettingsError.RoomNotFound => NotFound("Room not found."),
                    UpdateRoomSettingsError.PlayerNotFound => NotFound("Player not found."),
                    UpdateRoomSettingsError.NotHost => StatusCode(StatusCodes.Status403Forbidden, "You are not the host."),
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
