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

namespace Toko.Services
{
    public class RoomManager
    {
        // 并发安全的房间字典
        private readonly ConcurrentDictionary<string, Room> _rooms = new();
        //private readonly ConcurrentDictionary<string, byte> _activeRooms = new();


        private readonly IMediator _mediator;

        //private readonly IMemoryCache _cache;
        private static readonly TimeSpan ROOM_TTL = TimeSpan.FromMinutes(1);

        //public RoomManager(IMediator mediator, IMemoryCache cache)
        public RoomManager(IMediator mediator)
        {
            _mediator = mediator;
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
            var room = new Room(_mediator, stepsPerRound)
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
            //if (room.Racers.Count >= room.MaxPlayers) return JoinRoomError.RoomFull;

            //var racer = new Racer { Id = Guid.NewGuid().ToString(), PlayerName = playerName };
            //InitializeDeck(racer);
            //DrawCardsInternal(racer, 3);
            //room.Racers.Add(racer);
            //return new JoinRoomSuccess(racer);
            return await room.JoinRoomAsync(playerId, playerName);
        }

        public record DrawCardsSuccess(List<Card> Cards);
        public enum DrawCardsError { RoomNotFound, PlayerNotFound }

        /// <summary>
        /// 给某玩家抽牌：抽取张数 = min(requestedCount, 空余手牌槽数)
        /// </summary>
        /// 
        public OneOf<DrawCardsSuccess, DrawCardsError> DrawCards(
            string roomId, string playerId, int requestedCount)
        {
            var room = GetRoom(roomId);
            if (room is null) return DrawCardsError.RoomNotFound;
            var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer is null) return DrawCardsError.PlayerNotFound;
            // 计算还能抽多少张
            int space = racer.HandCapacity - racer.Hand.Count;
            int toDraw = Math.Min(requestedCount, Math.Max(0, space));
            var drawn = DrawCardsInternal(racer, toDraw);
            return new DrawCardsSuccess(drawn);
        }


        /// <summary>
        /// Internal: 实际从 Deck 抽卡，不作空槽检查，返回抽到的卡
        /// </summary>
        private List<Card> DrawCardsInternal(Racer racer, int count)
        {
            var drawn = new List<Card>();
            for (int i = 0; i < count; i++)
            {
                // 如果牌堆空了，就洗弃牌堆回去
                if (!racer.Deck.Any())
                {
                    foreach (var c in racer.DiscardPile) racer.Deck.Enqueue(c);
                    racer.DiscardPile.Clear();
                }
                if (!racer.Deck.Any()) break;

                var card = racer.Deck.Dequeue();
                racer.Hand.Add(card);
                drawn.Add(card);
            }
            return drawn;
        }

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
        /// 初始化一副标准牌堆：根据需要填充不同类型卡
        /// </summary>
        private void InitializeDeck(Racer racer)
        {
            // 清空旧牌
            racer.Deck.Clear();
            racer.Hand.Clear();
            racer.DiscardPile.Clear();


            void Add(CardType type, int qty)
            {
                for (int i = 0; i < qty; i++)
                    racer.Deck.Enqueue(new Card { Type = type });
            }

            Add(CardType.Move, 9);
            Add(CardType.ChangeLane, 9);
            Add(CardType.Repair, 4);

            // 最后随机洗牌
            // Fisher–Yates shuffle
            var rnd = new Random();
            var all = racer.Deck.ToList();
            ShuffleUtils.Shuffle(all);
            racer.Deck.Clear();
            foreach (var card in all)
                racer.Deck.Enqueue(card);
        }


        /// <summary>
        /// 标记房间为已开始
        /// </summary>
        public record StartRoomSuccess(string RoomId);
        public enum StartRoomError { RoomNotFound, AlreadyStarted, AlreadyFinished, NoPlayers, NotHost, AlreadyPlayingInAnotherRoom }
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

        public static class ShuffleUtils
        {
            //private static readonly Random _random = new Random(); // 避免每次创建新 Random 实例

            public static void Shuffle<T>(IList<T> list)
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = Random.Shared.Next(i + 1); // 0 ≤ j ≤ i
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
        }

        //private string IssueToken(string playerId, string roomId)
        //{
        //    var creds = new SigningCredentials(
        //        new SymmetricSecurityKey(
        //            Encoding.UTF8.GetBytes(_config["Jwt:Key"])),
        //        SecurityAlgorithms.HmacSha256);

        //    var token = new JwtSecurityToken(
        //        claims: new[]
        //        {
        //    new Claim(JwtRegisteredClaimNames.Sub, playerId),
        //    new Claim("room", roomId)          // 可选：锁定房间
        //        },
        //        expires: DateTime.UtcNow.AddDays(30),  // ← 30 天记住你
        //        signingCredentials: creds);

        //    return new JwtSecurityTokenHandler().WriteToken(token);
        //}
    }
}
