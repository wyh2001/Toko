using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using OneOf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Toko.Models;
using static Toko.Models.Room;
using static Toko.Services.CardHelper;

namespace Toko.Services
{
    public class RoomManager(IMediator mediator, ILogger<RoomManager> log, ILoggerFactory loggerFactory, IMemoryCache cache)
    {
        // 并发安全的房间字典
        private readonly ConcurrentDictionary<string, Room> _rooms = new();
        private readonly ILogger<RoomManager> _log = log;
        private readonly ILoggerFactory _loggerFactory = loggerFactory;
        //private readonly ConcurrentDictionary<string, string> _playersInPlay = new(); // playerId -> roomId
        //private readonly ConcurrentDictionary<string, byte> _activeRooms = new();


        private readonly IMediator _mediator = mediator;

        private readonly IMemoryCache _cache = cache;
        private static readonly TimeSpan ROOM_TTL = TimeSpan.FromMinutes(30);
        private long _waitingRoomsCount = 0;
        private long _normallyCompletedRoomsCount = 0;

        /// <summary>
        /// Get the current count of waiting rooms
        /// </summary>
        public long GetWaitingRoomsCount() => Interlocked.Read(ref _waitingRoomsCount);

        /// <summary>
        /// Get the count of rooms that completed normally (player crossed the finish line)
        /// </summary>
        public long GetNormallyCompletedRoomsCount() => Interlocked.Read(ref _normallyCompletedRoomsCount);

        /// <summary>
        /// 创建房间，并为房主生成一辆赛车
        /// </summary>
        public (string roomId, Racer hostRacer) CreateRoom(
            string playerId,
            string? roomName,
            int maxPlayers,
            bool isPrivate,
            string playerName,
            //int totalRounds,
            List<int> stepsPerRound)
        {
            //var roomId = Guid.NewGuid().ToString();
            // iloggerFactory 
            var roomLogger = _loggerFactory.CreateLogger<Room>();
            var room = new Room(_mediator, stepsPerRound, roomLogger, _loggerFactory)
            {
                //Id = roomId,
                Name = roomName ?? $"Room-{Random.Shared.Next(1000, 9999)}",
                MaxPlayers = maxPlayers,
                IsPrivate = isPrivate,
                Map = RaceMapFactory.CreateDefaultMap()
            };
            var roomId = room.Id;

            // 为房主生成 Racer，并抽初始手牌
            var host = new Racer
            {
                Id = playerId,
                PlayerName = playerName,
                IsHost = true,
            };
            InitializeDeck(host);           // 洗牌、填充初始牌堆
            DrawCardsInternal(host, INITIALDRAW);     // 假设开局抽 3 张卡

            room.Racers.Add(host);
            _rooms.TryAdd(roomId, room);

            // Increment waiting rooms count when a new room is created
            Interlocked.Increment(ref _waitingRoomsCount);

            // Here use MemoryCache to cache the room object
            _cache.Set(room.Id, room, new MemoryCacheEntryOptions
            {
                SlidingExpiration = ROOM_TTL
            }
            .RegisterPostEvictionCallback(async (key, value, reason, state) =>
            {
                if (value is Room rm)
                {
                    // If the room being evicted is in Waiting status, decrement the counter
                    if (rm.Status == RoomStatus.Waiting)
                    {
                        Interlocked.Decrement(ref _waitingRoomsCount);
                    }
                    await rm.DisposeAsync();
                }
            }));

            _log.LogInformation("Room {RoomId} created", room.Id);
            return (roomId, host);
        }

        /// <summary>
        /// 根据 ID 获取房间
        /// </summary>
        //public Room? GetRoom(string roomId) =>
        //_rooms.TryGetValue(roomId, out var room) ? room : null;
        private Room? GetRoomInternal(string roomId)
        {
            if (!_cache.TryGetValue(roomId, out Room? room))
            {
                _rooms.TryRemove(roomId, out _);
                return null;
            }
            return room;
        }

        public RoomStatus? GetRoomStatus(string roomId)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return null;
            return room.Status;
        }


        public record JoinRoomSuccess(Racer Racer, string RoomId);
        public enum JoinRoomError { RoomFull, RoomNotFound, AlreadyJoined }

        public async Task<OneOf<JoinRoomSuccess, JoinRoomError>> JoinRoom(
           string roomId, string playerId, string playerName)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return JoinRoomError.RoomNotFound;
            return await room.JoinRoomAsync(playerId, playerName);
        }

        public record DrawCardsSuccess(List<Card> Cards);
        public enum DrawCardsError { RoomNotFound, PlayerNotFound }

        public record SubmitStepCardSuccess(string RoomId, string PlayerId, string CardId);
        public enum SubmitStepCardError { RoomNotFound, PlayerNotFound, NotYourTurn, CardNotFound, WrongPhase, PlayerBanned, InvalidCardType }
        public async Task<OneOf<SubmitStepCardSuccess, SubmitStepCardError>> SubmitStepCard(
            string roomId, string playerId, string cardId)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return SubmitStepCardError.RoomNotFound;

            return await room.SubmitStepCardAsync(playerId, cardId);
        }

        /// <summary>
        /// 获取所有房间（用于大厅列表）
        /// </summary>
        public List<Room> GetAllRooms() =>
            _rooms.Values.ToList();

        // … 其他如 SetMap/GetMap/ExecuteTurn 等方法按需保留 …

        /// <summary>
        /// 标记房间为已开始
        /// </summary>
        public record StartRoomSuccess(string RoomId);
        public enum StartRoomError { RoomNotFound, AlreadyStarted, AlreadyFinished, NoPlayers, NotHost, NotAllReady, NotInTheRoom }
        public async Task<OneOf<StartRoomSuccess, StartRoomError>> StartRoom(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return StartRoomError.RoomNotFound;

            var result = await room.StartGameAsync(playerId);

            // If the room was successfully started, decrement the waiting rooms counter
            if (result.IsT0)
            {
                Interlocked.Decrement(ref _waitingRoomsCount);
            }

            return result;
        }

        public async Task<bool> EndRoom(string roomId, GameEndReason reason)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return false;

            if (room.Status == RoomStatus.Waiting)
            {
                Interlocked.Decrement(ref _waitingRoomsCount);
            }

            if (reason == GameEndReason.FinisherCrossedLine)
            {
                Interlocked.Increment(ref _normallyCompletedRoomsCount);
                _log.LogInformation("Room {RoomId} completed normally with player crossing finish line", roomId);
            }

            await room.EndGameAsync(reason);
            return true;
        }

        public record SubmitExecutionParamSuccess(string RoomId, string PlayerId, ConcreteInstruction Instruction);
        public enum SubmitExecutionParamError { RoomNotFound, PlayerNotFound, NotYourTurn, CardNotFound, InvalidExecParameter, WrongPhase, PlayerBanned }
        public async Task<OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>> SubmitExecutionParam(
            string roomId, string playerId, ExecParameter execParameter)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return SubmitExecutionParamError.RoomNotFound;

            return await room.SubmitExecutionParamAsync(playerId, execParameter);
        }


        public record DiscardCardsSuccess(string RoomId, string PlayerId, List<string> CardIds);
        public enum DiscardCardsError { RoomNotFound, PlayerNotFound, NotYourTurn, CardNotFound, PlayerBanned, WrongPhase, InternalError, InvalidCardType }
        public async Task<OneOf<DiscardCardsSuccess, DiscardCardsError>> DiscardCards(
            string roomId, string playerId, List<string> cardIds)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return DiscardCardsError.RoomNotFound;

            return await room.SubmitDiscardAsync(playerId, cardIds);
        }


        public record LeaveRoomSuccess(string RoomId, string PlayerId);
        public enum LeaveRoomError { RoomNotFound, PlayerNotFound, AlreadyFinished, InternalError }
        public async Task<OneOf<LeaveRoomSuccess, LeaveRoomError>> LeaveRoom(string roomId, string playerId)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return LeaveRoomError.RoomNotFound;
            return await room.LeaveRoomAsync(playerId);
        }

        public record ReadyUpSuccess(string RoomId, string PlayerId, bool IsReady);
        public enum ReadyUpError { RoomNotFound, PlayerNotFound, InternalError }
        public async Task<OneOf<ReadyUpSuccess, ReadyUpError>> ReadyUp(string roomId, string playerId, bool isReady)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return ReadyUpError.RoomNotFound;
            return await room.ReadyUpAsync(playerId, isReady);
        }

        public record DrawSkipSuccess(string RoomId, string PlayerId, IReadOnlyList<Card> DrawnCards);
        public enum DrawSkipError
        {
            RoomNotFound,
            PlayerNotFound,
            NotYourTurn,
            //HandFull,
            WrongPhase,
            PlayerBanned
        }
        public async Task<OneOf<DrawSkipSuccess, DrawSkipError>> DrawSkip(string roomId, string playerId)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return DrawSkipError.RoomNotFound;
            return await room.DrawSkipAsync(playerId);
        }

        public record GetHandSuccess(string RoomId, string PlayerId, List<Card> Hand);
        public enum GetHandError { RoomNotFound, PlayerNotFound, InternalError }
        public async Task<OneOf<GetHandSuccess, GetHandError>> GetHand(string roomId, string playerId)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return GetHandError.RoomNotFound;
            return await room.GetHandAsync(playerId);
        }

        public record GetRoomStatusSuccess(Room.RoomStatusSnapshot Snapshot);
        public enum GetRoomStatusError { RoomNotFound }

        public async Task<OneOf<GetRoomStatusSuccess, GetRoomStatusError>> GetRoomStatusAsync(string roomId)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return GetRoomStatusError.RoomNotFound;
            var snapshot = await room.GetStatusSnapshotAsync();
            return new GetRoomStatusSuccess(snapshot);
        }

        public enum KickPlayerError { RoomNotFound, WrongPhase, TooEarly, TargetNotFound, AlreadyKicked, PlayerNotFound, NotHost }
        public record KickPlayerSuccess(string RoomId, string PlayerId, string KickedPlayerId);
        public async Task<OneOf<KickPlayerSuccess, KickPlayerError>> KickPlayer(string roomId, string playerId, string kickedPlayerId)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return KickPlayerError.RoomNotFound;
            return await room.KickPlayerAsync(playerId, kickedPlayerId);
        }

        public enum UpdateRoomSettingsError { RoomNotFound, NotHost, InternalError, WrongPhase, PlayerNotFound }
        public record UpdateRoomSettingsSuccess(string RoomId, string PlayerId, RoomSettings Settings);
        //public record RoomSettings(string? Name, int MaxPlayers, bool IsPrivate, List<int> StepsPerRound, RaceMap Map);
        public record RoomSettings(string? Name, int? MaxPlayers, bool? IsPrivate, List<int>? StepsPerRound); // at this time, no map for simplicity
        public async Task<OneOf<UpdateRoomSettingsSuccess, UpdateRoomSettingsError>> UpdateRoomSettings(
            string roomId, string playerId, RoomSettings settings)
        {
            var room = GetRoomInternal(roomId);
            if (room is null) return UpdateRoomSettingsError.RoomNotFound;
            return await room.UpdateSettingsAsync(playerId, settings);
        }
    }
}
