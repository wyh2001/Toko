using MediatR;
using OneOf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Toko.Models;
using static Toko.Models.Room;

namespace Toko.Services
{
    public class RoomManager
    {
        // 并发安全的房间字典
        private readonly ConcurrentDictionary<string, Room> _rooms = new();


        private readonly IMediator _mediator;

        public RoomManager(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// 创建房间，并为房主生成一辆赛车
        /// </summary>
        public (string roomId, Racer hostRacer) CreateRoom(
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
                //Id = Guid.NewGuid().ToString(),
                PlayerName = playerName
            };
            InitializeDeck(host);           // 洗牌、填充初始牌堆
            DrawCardsInternal(host, 3);     // 假设开局抽 3 张卡

            room.Racers.Add(host);
            _rooms.TryAdd(roomId, room);
            return (roomId, host);
        }

        /// <summary>
        /// 根据 ID 获取房间
        /// </summary>
        public Room? GetRoom(string roomId) =>
            _rooms.TryGetValue(roomId, out var room) ? room : null;


        public record JoinRoomSuccess(Racer Racer);
        public enum JoinRoomError { RoomFull, RoomNotFound }

        public OneOf<JoinRoomSuccess, JoinRoomError> JoinRoom(
           string roomId, string playerName)
        {
            var room = GetRoom(roomId);
            if (room is null) return JoinRoomError.RoomNotFound;
            if (room.Racers.Count >= room.MaxPlayers) return JoinRoomError.RoomFull;

            var racer = new Racer { Id = Guid.NewGuid().ToString(), PlayerName = playerName };
            InitializeDeck(racer);
            DrawCardsInternal(racer, 3);
            room.Racers.Add(racer);
            return new JoinRoomSuccess(racer);
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
        public OneOf<SubmitStepCardSuccess, SubmitStepCardError> SubmitStepCard(
            string roomId, string playerId, string cardId)
        {
            var room = GetRoom(roomId);
            if (room is null) return SubmitStepCardError.RoomNotFound;
            //var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
            //if (racer is null) return SubmitStepCardError.PlayerNotFound;
            //if (room.CurrentStep != step) return SubmitStepCardError.NotYourTurn;
            return room.SubmitStepCard(playerId, cardId);
            //// 1) 验证卡牌在手牌里
            //if (!racer.Hand.Any(c => c.Id == cardId))
            //    return SubmitStepCardError.CardNotFound;
            //var dict = room.StepCardSubmissions
            //               .GetOrAdd(step, _ => new ConcurrentDictionary<string, string>());
            //dict[playerId] = cardId;
            //return new SubmitStepCardSuccess(cardId);
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
        public enum StartRoomError { RoomNotFound, AlreadyStarted, AlreadyFinished, NoPlayers }
        public OneOf<StartRoomSuccess, StartRoomError> StartRoom(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return StartRoomError.RoomNotFound;
            //if (room.Status == RoomStatus.Playing)
            //    return StartRoomError.AlreadyStarted;
            //if (room.Status == RoomStatus.Finished)
            //    return StartRoomError.AlreadyFinished;
            //room.Status = RoomStatus.Playing;
            //room.CurrentRound = 1;
            //room.CurrentStep = 0;
            //room.StartGame();
            //return new StartRoomSuccess(roomId);
            return room.StartGame();
        }

        public bool EndRoom(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return false;
            //room.IsGameFinished = true;
            //room.Status = RoomStatus.Finished;
            room.EndGame();
            return true;
        }

        public record SubmitExecutionParamSuccess(ConcreteInstruction Instruction);
        public enum SubmitExecutionParamError { RoomNotFound, PlayerNotFound, NotYourTurn, CardNotFound, InvalidExecParameter, WrongPhase,
            PlayerBanned
        }

        public OneOf<SubmitExecutionParamSuccess, SubmitExecutionParamError> SubmitExecutionParam(
            string roomId, string playerId, ExecParameter execParameter)
        {
            var room = GetRoom(roomId);
            if (room is null) return SubmitExecutionParamError.RoomNotFound;
            //var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
            //if (racer is null) return SubmitExecutionParamError.PlayerNotFound;
            //if (!room.StepCardSubmissions.TryGetValue(step, out var cardDict)) return SubmitExecutionParamError.NotYourTurn;
            //if (!cardDict.TryGetValue(playerId, out var cardId)) return SubmitExecutionParamError.CardNotFound;
            // 1) 根据 cardId 找到原 CardType
            //    假设你在卡片提交时已经把 CardType 一起存进字典，或者
            //    这里你可以去 racer.Hand/DiscardPile 里找这张卡
            //var allCards = racer.Hand.Concat(racer.DiscardPile);
            //var card = allCards.First(c => c.Id == cardId);
            //if (card == null) return SubmitExecutionParamError.CardNotFound;

            //switch (card.Type)
            //{
            //    case CardType.Move:
            //        if (execParameter.Effect >= 0 && execParameter.Effect <= 2)
            //            break;
            //        else
            //            return SubmitExecutionParamError.InvalidExecParameter;
            //    case CardType.ChangeLane:
            //        if (execParameter.Effect == 1 || execParameter.Effect == -1)
            //            break;
            //        else
            //            return SubmitExecutionParamError.InvalidExecParameter;
            //    case CardType.Repair:
            //        if (execParameter.DiscardedCardIds == null || !execParameter.DiscardedCardIds.Any())
            //            return SubmitExecutionParamError.InvalidExecParameter;
            //        break;
            //    default:
            //        return SubmitExecutionParamError.InvalidExecParameter;
            //}

            //// 2) 构造 ConcreteInstruction
            //var ins = new ConcreteInstruction
            //{
            //    Type = card.Type,
            //    ExecParameter = execParameter
            //};
            //// 3) 立即执行此条指令
            //var executor = new TurnExecutor(room.Map!);
            //executor.ApplyInstruction(racer, ins, room);
            //// 4) 记录执行过的指令
            //var execDict = room.StepExecSubmissions
            //                   .GetOrAdd(step, _ => new ConcurrentDictionary<string, ConcreteInstruction>());
            //execDict[playerId] = ins;
            //return new SubmitExecutionParamSuccess(ins, racer.SegmentIndex, racer.LaneIndex);

            return room.SubmitExecutionParam(playerId, execParameter);
        }


        public record DiscardCardsSuccess(List<string> cardIds);
        public enum DiscardCardsError { RoomNotFound, PlayerNotFound, NotYourTurn, CardNotFound, PlayerBanned, WrongPhase, InternalError }
        public OneOf<DiscardCardsSuccess, DiscardCardsError> DiscardCards(
            string roomId, string playerId, int step, List<string> cardIds)
        {
            var room = GetRoom(roomId);
            if (room is null) return DiscardCardsError.RoomNotFound;
            var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer is null) return DiscardCardsError.PlayerNotFound;
            //if (room.CurrentStep != step) return DiscardCardsError.NotYourTurn;
            // 1) 验证卡牌在手牌里
            //if (!racer.Hand.Any(c => cardIds.Contains(c.Id)))
            //    return DiscardCardsError.CardNotFound;
            //var executor = new TurnExecutor(room.Map);
            //if (!executor.DiscardCards(racer, cardIds, room))
            //    return DiscardCardsError.InternalError;
            //return new DiscardCardsSuccess(cardIds);
            return room.SubmitDiscard(playerId, cardIds);
        }


        public record LeaveRoomSuccess(string PlayerId);
        public enum LeaveRoomError { RoomNotFound, PlayerNotFound, InternalError }
        //internal bool LeaveRoom(string roomId, string playerId)
        public OneOf<LeaveRoomSuccess, LeaveRoomError> LeaveRoom(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return LeaveRoomError.RoomNotFound;
            var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer is null)
                return LeaveRoomError.PlayerNotFound;
            if (room.Racers.Remove(racer))
                return new LeaveRoomSuccess(playerId);
            else
                return LeaveRoomError.InternalError;
        }

        public static class ShuffleUtils
        {
            private static readonly Random _random = new Random(); // 避免每次创建新 Random 实例

            public static void Shuffle<T>(IList<T> list)
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = _random.Next(i + 1); // 0 ≤ j ≤ i
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
        }
    }
}
