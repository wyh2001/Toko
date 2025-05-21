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
using System.Threading.Tasks;
using Toko.Models;
using static Toko.Models.Room;
using static Toko.Services.CardHelper;

namespace Toko.Services
{
    public class RoomManager
    {
        // 并发安全的房间字典
        private readonly ConcurrentDictionary<string, Room> _rooms = new();
        private readonly ILogger<RoomManager> _log;
        private readonly ILoggerFactory _loggerFactory;
        //private readonly ConcurrentDictionary<string, string> _playersInPlay = new(); // playerId -> roomId
        //private readonly ConcurrentDictionary<string, byte> _activeRooms = new();


        private readonly IMediator _mediator;

        //private readonly IMemoryCache _cache;
        private static readonly TimeSpan ROOM_TTL = TimeSpan.FromMinutes(1);

        //public RoomManager(IMediator mediator, IMemoryCache cache)
        public RoomManager(IMediator mediator, ILogger<RoomManager> log, ILoggerFactory loggerFactory)
        {
            _mediator = mediator;
            _log = log;
            _loggerFactory = loggerFactory;
            //_cache = cache;
        }

        /// <summary>
        /// 创建房间，并为房主生成一辆赛车
        /// </summary>
        public (string roomId, Racer hostRacer) CreateRoom(
            string playerId,
            string? roomName,
            int maxPlayers,
            bool isPrivate,
            string playerName,
            int totalRounds,
            List<int> stepsPerRound)
        {
            //var roomId = Guid.NewGuid().ToString();
            // iloggerFactory 
            var roomLogger = _loggerFactory.CreateLogger<Room>();
            var room = new Room(_mediator, stepsPerRound, roomLogger)
            {
                //Id = roomId,
                Name = roomName,
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
            DrawCardsInternal(host, 3);     // 假设开局抽 3 张卡

            room.Racers.Add(host);
            _rooms.TryAdd(roomId, room);

            // Here use MemoryCache to cache the room object
            //_cache.Set(room.Id, room, new MemoryCacheEntryOptions
            //{
            //    SlidingExpiration = ROOM_TTL,
            //    PostEvictionCallbacks =
            //    {
            //        new PostEvictionCallbackRegistration
            //        {
            //            EvictionCallback = (_, value, reason, _) =>
            //            {
            //                // reason == TokenExpired / Removed / Replaced / Expired …
            //                (value as Room)?.Dispose();
            //                _activeRooms.TryRemove(room.Id, out _);
            //                //_logger.LogInformation(
            //                //    "Room {RoomId} evicted from cache: {Reason}", room.Id, reason);
            //                Console.WriteLine($"Room {room.Id} evicted from cache: {reason}");
            //            }
            //        }
            //    }
            //});
            //_activeRooms[room.Id] = 0;
            _log.LogInformation("Room {RoomId} created", room.Id);
            return (roomId, host);
        }

        /// <summary>
        /// 根据 ID 获取房间
        /// </summary>
        public Room? GetRoom(string roomId) =>
            _rooms.TryGetValue(roomId, out var room) ? room : null;


        public record JoinRoomSuccess(Racer Racer);
        public enum JoinRoomError { RoomFull, RoomNotFound }

        public async Task<OneOf<JoinRoomSuccess, JoinRoomError>> JoinRoom(
           string roomId, string playerId, string playerName)
        {
            var room = GetRoom(roomId);
            if (room is null) return JoinRoomError.RoomNotFound;
            return await room.JoinRoomAsync(playerId, playerName);
        }

        public record DrawCardsSuccess(List<Card> Cards);
        public enum DrawCardsError { RoomNotFound, PlayerNotFound }

        /// <summary>
        /// 给某玩家抽牌：抽取张数 = min(requestedCount, 空余手牌槽数)
        /// </summary>
        /// 
        //public OneOf<DrawCardsSuccess, DrawCardsError> DrawCards(
        //    string roomId, string playerId, int requestedCount)
        //{
        //    var room = GetRoom(roomId);
        //    if (room is null) return DrawCardsError.RoomNotFound;
        //    var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
        //    if (racer is null) return DrawCardsError.PlayerNotFound;
        //    // 计算还能抽多少张
        //    int space = racer.HandCapacity - racer.Hand.Count;
        //    int toDraw = Math.Min(requestedCount, Math.Max(0, space));
        //    var drawn = DrawCardsInternal(racer, toDraw);
        //    return new DrawCardsSuccess(drawn);
        //}

        public record SubmitStepCardSuccess(string CardId);
        public enum SubmitStepCardError { RoomNotFound, PlayerNotFound, NotYourTurn, CardNotFound, WrongPhase,
            PlayerBanned
        }
        public async Task<OneOf<SubmitStepCardSuccess, SubmitStepCardError>> SubmitStepCard(
            string roomId, string playerId, string cardId)
        {
            var room = GetRoom(roomId);
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
        public enum StartRoomError { RoomNotFound, AlreadyStarted, AlreadyFinished, NoPlayers, NotHost, NotAllReady }
        public async Task<OneOf<StartRoomSuccess, StartRoomError>> StartRoom(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return StartRoomError.RoomNotFound;

                return await room.StartGameAsync(playerId);
        }

        public async Task<bool> EndRoom(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return false;

            // Await the asynchronous call to ensure proper execution order
            await room.EndGameAsync();
            return true;
        }

        public record SubmitExecutionParamSuccess(ConcreteInstruction Instruction);
        public enum SubmitExecutionParamError { RoomNotFound, PlayerNotFound, NotYourTurn, CardNotFound, InvalidExecParameter, WrongPhase,
            PlayerBanned
        }

        public async Task<OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError>> SubmitExecutionParam(
            string roomId, string playerId, ExecParameter execParameter)
        {
            var room = GetRoom(roomId);
            if (room is null) return SubmitExecutionParamError.RoomNotFound;

            return await room.SubmitExecutionParamAsync(playerId, execParameter);
        }


        public record DiscardCardsSuccess(List<string> cardIds);
        public enum DiscardCardsError { RoomNotFound, PlayerNotFound, NotYourTurn, CardNotFound, PlayerBanned, WrongPhase, InternalError }
        public async Task<OneOf<DiscardCardsSuccess, DiscardCardsError>> DiscardCards(
            string roomId, string playerId, List<string> cardIds)
        {
            var room = GetRoom(roomId);
            if (room is null) return DiscardCardsError.RoomNotFound;
            //var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
            //if (racer is null) return DiscardCardsError.PlayerNotFound;

            return await room.SubmitDiscardAsync(playerId, cardIds);
        }


        public record LeaveRoomSuccess(string PlayerId);
        public enum LeaveRoomError { RoomNotFound, PlayerNotFound, InternalError }
        //internal bool LeaveRoom(string roomId, string playerId)
        public async Task<OneOf<LeaveRoomSuccess, LeaveRoomError>> LeaveRoom(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return LeaveRoomError.RoomNotFound;
            //var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
            //if (racer is null)
            //    return LeaveRoomError.PlayerNotFound;
            //if (room.Racers.Remove(racer))
            //    return new LeaveRoomSuccess(playerId);
            //else
            //    return LeaveRoomError.InternalError;

            return await room.LeaveRoomAsync(playerId);
        }

        public record ReadyUpSuccess(string PlayerId, bool IsReady);
        public enum ReadyUpError { RoomNotFound, PlayerNotFound, InternalError }
        public async Task<OneOf<ReadyUpSuccess, ReadyUpError>> ReadyUp(string roomId, string playerId, bool isReady)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return ReadyUpError.RoomNotFound;
            return await room.ReadyUpAsync(playerId, isReady);
        }

        public record DrawSkipSuccess(IReadOnlyList<Card> DrawnCards);
        public enum DrawSkipError
        {
            RoomNotFound,
            PlayerNotFound,
            NotYourTurn,
            HandFull,
            WrongPhase,
            PlayerBanned
        }
        public async Task<OneOf<DrawSkipSuccess, DrawSkipError>> DrawSkip(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return DrawSkipError.RoomNotFound;
            return await room.DrawSkipAsync(playerId);
        }


    }
}
