using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Toko.Models;

namespace Toko.Services
{
    public class RoomManager
    {
        // 并发安全的房间字典
        private readonly ConcurrentDictionary<string, Room> _rooms = new();

        /// <summary>
        /// 创建房间，并为房主生成一辆赛车
        /// </summary>
        public (string roomId, Racer hostRacer) CreateRoom(
            string? roomName,
            int maxPlayers,
            bool isPrivate,
            string playerName)
        {
            var roomId = Guid.NewGuid().ToString();
            var room = new Room
            {
                Id = roomId,
                Name = roomName,
                MaxPlayers = maxPlayers,
                IsPrivate = isPrivate,
                Map = RaceMapFactory.CreateDefaultMap()
            };

            // 为房主生成 Racer，并抽初始手牌
            var host = new Racer
            {
                Id = Guid.NewGuid().ToString(),
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

        /// <summary>
        /// 玩家加入房间：生成新 Racer、抽初始手牌
        /// </summary>
        public (bool success, Racer? racer) JoinRoom(string roomId, string playerName)
        {
            if (!_rooms.TryGetValue(roomId, out var room) 
                || room.Racers.Count >= room.MaxPlayers)
                return (false, null);

            var racer = new Racer
            {
                Id = Guid.NewGuid().ToString(),
                PlayerName = playerName
            };
            InitializeDeck(racer);
            DrawCardsInternal(racer, 3);

            room.Racers.Add(racer);
            return (true, racer);
        }

        /// <summary>
        /// 给某玩家抽牌：抽取张数 = min(requestedCount, 空余手牌槽数)
        /// </summary>
        public List<Card> DrawCards(string roomId, string playerId, int requestedCount)
        {
            var room = GetRoom(roomId);
            var racer = room?.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer == null) return new();

            // 计算还能抽多少张
            int space = racer.HandCapacity - racer.Hand.Count;
            int toDraw = Math.Min(requestedCount, Math.Max(0, space));

            return DrawCardsInternal(racer, toDraw);
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

        public bool SubmitStepCard(string roomId, string playerId, int step, string cardId)
        {
            var room = GetRoom(roomId);

            // … 验证step==room.CollectingStep、cardId 在手牌里 …
            // 1) 验证房间和玩家
            if (room == null || !room.Racers.Any(r => r.Id == playerId))
                return false;
            // 2) 验证当前步骤
            if (room.CollectingStep != step)
                return false;
            // 3) 验证卡牌在手牌里
            var racer = room.Racers.First(r => r.Id == playerId);
            if (!racer.Hand.Any(c => c.Id == cardId))
                return false;


            var dict = room.StepCardSubmissions
                           .GetOrAdd(step, _ => new ConcurrentDictionary<string, string>());
            dict[playerId] = cardId;
            return true;
            //// 返回本 step 已交的所有 cardId 按玩家顺序
            //return room.Racers
            //           .Where(r => dict.ContainsKey(r.Id))
            //           .Select(r => dict[r.Id])
            //           .ToList();
        }

        ///// <summary>
        ///// 玩家提交本回合要打出的卡牌；cardIds 为空表示“不出牌”
        ///// </summary>
        //public bool SubmitCards(string roomId, string playerId, List<string> cardIds)
        //{
        //    var room = GetRoom(roomId);
        //    var racer = room?.Racers.FirstOrDefault(r => r.Id == playerId);
        //    if (room == null || racer == null) return false;

        //    // 1) 允许不出牌：清空指令列表
        //    if (cardIds == null || cardIds.Count == 0)
        //    {
        //        room.SubmittedInstructions[playerId] = new List<CardType>();
        //        return true;
        //    }

        //    // 2) 验证选中卡都在手牌中
        //    var selected = racer.Hand.Where(c => cardIds.Contains(c.Id)).ToList();
        //    if (selected.Count != cardIds.Count)
        //        return false; // 选了不存在的卡

        //    // 3) 处理 Repair：每张 Repair 丢弃一张 Junk
        //    var repairs = selected.Where(c => c.Type == CardType.Repair).ToList();
        //    foreach (var rep in repairs)
        //    {
        //        racer.Hand.Remove(rep);
        //        racer.DiscardPile.Add(rep);

        //        // 丢弃一张 Junk（若有）
        //        var junk = racer.Hand.FirstOrDefault(c => c.Type == CardType.Junk);
        //        if (junk != null)
        //        {
        //            racer.Hand.Remove(junk);
        //            racer.DiscardPile.Add(junk);
        //        }
        //    }

        //    // 4) 保证剩余选中卡都是可执行指令
        //    var playables = selected.Except(repairs).ToList();
        //    if (playables.Any(c => c.Type == CardType.Junk))
        //        return false;  // 绝不允许出 Junk

        //    // 5) 转成 CardId 列表
        //    var selectedCards= playables
        //        .Select(c => c.Id)
        //        .ToList();

        //    // 6) 把打出的指令卡移入弃牌堆
        //    foreach (var c in playables)
        //    {
        //        racer.Hand.Remove(c);
        //        racer.DiscardPile.Add(c);
        //    }

        //    // 7) 存储本回合指令
        //    //room.SubmittedInstructions[playerId] = instructions;
        //    room.SelectedCards[playerId] = selectedCards;
        //    return true;
        //}

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

            // 示例：每种动作卡各 3 张，Junk 5 张，Repair 2 张
            void Add(CardType type, int qty)
            {
                for (int i = 0; i < qty; i++)
                    racer.Deck.Enqueue(new Card { Type = type });
            }

            //Add(CardType.Forward1, 3);
            //Add(CardType.Forward2, 2);
            //Add(CardType.ChangeLeft, 2);
            //Add(CardType.ChangeRight, 2);
            //Add(CardType.ChangeLeft_Forward1, 1);
            //Add(CardType.ChangeRight_Forward1, 1);
            //Add(CardType.Forward1_ChangeLeft, 1);
            //Add(CardType.Forward1_ChangeRight, 1);
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
        public bool StartRoom(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room) || room.Status == RoomStatus.Playing || room.Status == RoomStatus.Finished)
                return false;
            //room.IsGameStarted = true;
            room.Status = RoomStatus.Playing;
            return true;
        }

        public bool EndRoom(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return false;
            //room.IsGameFinished = true;
            room.Status = RoomStatus.Finished;
            return true;
        }

        //public bool SubmitParameters(string roomId, string playerId, List<InstructionParamDto> @params)
        //{
        //    var room = GetRoom(roomId);
        //    if (room == null) return false;

        //    // 1) 必须先有选卡
        //    if (!room.SelectedCards.TryGetValue(playerId, out var sel)) return false;

        //    // 2) 验证每个 param 对应 sel 里的一张卡
        //    if (@params.Count != sel.Count ||
        //        @params.Any(p => !sel.Contains(p.CardId)))
        //        return false;

        //    // 3) 根据卡类型和参数生成具体指令
        //    var concrete = new List<ConcreteInstruction>();
        //    foreach (var p in @params)
        //    {
        //        // 取出卡牌对象
        //        var racer = room.Racers.First(r => r.Id == playerId);
        //        var card = racer.DiscardPile
        //                       .Concat(racer.Hand)
        //                       .First(c => c.Id == p.CardId);
        //        if (card.Type == CardType.Move)
        //        {
        //            if (p.Parameter <= 0) return false;
        //            concrete.Add(new ConcreteInstruction
        //            {
        //                Type = InstructionType.Move,
        //                Parameter = p.Parameter
        //            });
        //        }
        //        else if (card.Type == CardType.ChangeLane)
        //        {
        //            if (p.Parameter != -1 && p.Parameter != +1) return false;
        //            concrete.Add(new ConcreteInstruction
        //            {
        //                Type = InstructionType.ChangeLane,
        //                Parameter = p.Parameter
        //            });
        //        }
        //        else return false;
        //    }

        //    room.FinalInstructions[playerId] = concrete;
        //    return true;
        //}

        public ConcreteInstruction SubmitExecutionParam(string roomId, string playerId, int step, ExecParameter execParameter)
        {
            var room = GetRoom(roomId);
            var racer = room.Racers.First(r => r.Id == playerId);

            // 1) 拿到这一步玩家的 cardId
            var cardDict = room.StepCardSubmissions[step];
            if (!cardDict.TryGetValue(playerId, out var cardId))
                throw new InvalidOperationException("该玩家未提交此步卡牌");

            // 2) 根据 cardId 找到原 CardType
            //    假设你在卡片提交时已经把 CardType 一起存进字典，或者
            //    这里你可以去 racer.Hand/DiscardPile 里找这张卡

            var allCards = racer.Hand.Concat(racer.DiscardPile);
            var card = allCards.First(c => c.Id == cardId);

                // 3) 构造 ConcreteInstruction
                var ins = new ConcreteInstruction
                {
                Type = card.Type,
                ExecParameter = execParameter
                };

            // 4) 立即执行此条指令
            var executor = new TurnExecutor(room.Map!);
            executor.ApplyInstruction(racer, ins, room);

            // 5) 记录执行过的指令
            var execDict = room.StepExecSubmissions
                               .GetOrAdd(step, _ => new ConcurrentDictionary<string, ConcreteInstruction>());
            execDict[playerId] = ins;

            return ins;
        }

        public bool DiscardCards(string roomId, string playerId, int step, List<string> cardIds)
        {
            var room = GetRoom(roomId);
            var racer = room.Racers.First(r => r.Id == playerId);
            // check if step == room.CollectingStep
            if (room.CollectingStep != step)
                return false;
            var executor = new TurnExecutor(room.Map);
            return executor.DiscardCards(racer, cardIds, room);
        }

        internal bool LeaveRoom(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return false;
            var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
            if (racer == null)
                return false;
            return room.Racers.Remove(racer);
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
