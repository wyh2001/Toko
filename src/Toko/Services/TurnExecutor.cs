using System;
using System.Collections.Generic;
using System.Linq;
using Toko.Models;

namespace Toko.Services
{
    public class TurnExecutor(RaceMap map, ILogger<TurnExecutor> log)
    {
        private readonly RaceMap _map = map ?? throw new ArgumentNullException(nameof(map));
        public List<TurnLog> Logs { get; } = [];
        private readonly ILogger<TurnExecutor> _log = log;
        private const int MAX_INTERACTION_DEPTH = 5;

        public enum TurnExecutionResult
        {
            Continue,
            PlayerFinished,
            InvalidState
        }

        public TurnExecutionResult ApplyInstruction(Racer racer, ConcreteInstruction ins, Room room)
        {
            // 先执行动作
            switch (ins.Type)
            {
                case CardType.Move:
                    if (ins.ExecParameter.Effect <= 0 || ins.ExecParameter.Effect > 2)
                    {
                        return TurnExecutionResult.InvalidState;
                    }
                    return MoveForward(racer, ins.ExecParameter.Effect, room, 0); // Initial depth 0
                case CardType.ChangeLane:
                    if (ins.ExecParameter.Effect != -1 && ins.ExecParameter.Effect != 1)
                    {
                        return TurnExecutionResult.InvalidState;
                    }
                    ChangeLane(racer, ins.ExecParameter.Effect, room, 0); // Initial depth 0
                    return TurnExecutionResult.Continue;
                case CardType.Repair:
                    Repair(racer, ins.ExecParameter.DiscardedCardIds);
                    return TurnExecutionResult.Continue;
                default:
                    // Junk 不会在此提交
                    return TurnExecutionResult.Continue;
            }
        }

        private TurnExecutionResult MoveForward(Racer racer, int steps, Room room, int depth)
        {
            if (depth >= MAX_INTERACTION_DEPTH)
            {
                _log.LogWarning($"MoveForward for racer {racer.Id} reached max interaction depth of {MAX_INTERACTION_DEPTH}. Halting further movement in this step to prevent potential loop.");
                return TurnExecutionResult.Continue;
            }

            for (int i = 0; i < steps; i++)
            {
                // 先检查是否进入下一段
                var seg = _map.Segments[racer.SegmentIndex];
                if (racer.CellIndex + 1 >= seg.LaneCells[racer.LaneIndex].Count)
                {
                    // 如果是最后一段，则结束比赛
                    if (racer.SegmentIndex + 1 >= _map.Segments.Count)
                    {
                        return TurnExecutionResult.PlayerFinished;
                    }
                    // 下一段
                    racer.LaneIndex /= (seg.LaneCount / _map.Segments[racer.SegmentIndex + 1].LaneCount);
                    racer.SegmentIndex++;
                    racer.CellIndex = 0;
                    //racer.CellIndex++;
                }
                else
                {
                    // 否则继续在当前段前进
                    racer.CellIndex++;
                }
                // 碰撞检查：同段同道即撞人
                var collided = room.Racers
                    .Where(r => r.Id != racer.Id
                             && r.SegmentIndex == racer.SegmentIndex
                             && r.CellIndex == racer.CellIndex
                             && r.LaneIndex == racer.LaneIndex)
                    .ToList();

                if (collided.Count != 0)
                {
                    // 主动撞与被撞都得 Junk
                    AddJunk(racer, 1);
                    foreach (var other in collided)
                    {
                        AddJunk(other, 1);
                        // 被撞者前进一格
                        return MoveForward(other, 1, room, depth + 1); // Increment depth
                    }
                }
            }

            return TurnExecutionResult.Continue;
        }

        private void ChangeLane(Racer racer, int delta, Room room, int depth)
        {
            if (depth >= MAX_INTERACTION_DEPTH)
            {
                _log.LogWarning($"ChangeLane for racer {racer.Id} reached max interaction depth of {MAX_INTERACTION_DEPTH}. Halting further lane changes in this step to prevent potential loop.");
                return;
            }

            int newLane = racer.LaneIndex + delta;
            var seg = _map.Segments[racer.SegmentIndex];

            // 如果试图越过左右边界，则撞墙
            if (newLane < 0 || newLane >= seg.LaneCount)
            {
                AddJunk(racer, 1);
                return;
            }

            racer.LaneIndex = newLane;

            // 换道后立即做碰撞检测
            var collided = room.Racers
                .Where(r => r.Id != racer.Id
                         && r.SegmentIndex == racer.SegmentIndex
                         && r.CellIndex == racer.CellIndex
                         && r.LaneIndex == racer.LaneIndex)
                .ToList();
            if (collided.Count != 0)
            {
                AddJunk(racer, 1);
                foreach (var other in collided)
                {
                    AddJunk(other, 1);
                    ChangeLane(other, delta, room, depth + 1); // Increment depth
                }
            }
        }

        private static void AddJunk(Racer racer, int qty)
        {
            for (int i = 0; i < qty; i++)
                racer.Deck.Enqueue(new Card { Type = CardType.Junk });
        }

        private static void Repair(Racer racer, List<string> discardedCardId)
        {
            // see if the size is legit
            if (discardedCardId.Count > 2)
                return; // or throw?
            // check if the cards are in the hand
            foreach (var cardId in discardedCardId)
            {
                var card = racer.Hand
                    .FirstOrDefault(c => c.Id == cardId);
                if (card == null)
                    return; // not in hand
                if (card.Type != CardType.Junk)
                    return; // not junk
            }
            // discard the junk card the user chose
            InternalDiscard(racer, discardedCardId);
        }

        // dicard cards
        private static void InternalDiscard(Racer racer, List<string> discardedCardId)
        {
            // see if the size is legit
            if (discardedCardId.Count == 0)
                return;

            foreach (var cardId in discardedCardId)
            {
                var card = racer.Hand
                    .FirstOrDefault(c => c.Id == cardId);
                if (card != null)
                {
                    racer.Hand.Remove(card);
                    racer.DiscardPile.Add(card);
                }
            }
        }

        public bool DiscardCards(Racer racer, List<string> discardedCardId)
        {
            if (discardedCardId.Count == 0)
                return true;
            // check if the cards are in the hand
            foreach (var cardId in discardedCardId)
            {
                var card = racer.Hand
                    .FirstOrDefault(c => c.Id == cardId);
                if (card == null)
                    return false; // not all in hand
            }
            // discard the cards the user chose
            InternalDiscard(racer, discardedCardId);
            return true;
        }
    }
}
