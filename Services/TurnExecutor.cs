using System;
using System.Collections.Generic;
using System.Linq;
using Toko.Models;

namespace Toko.Services
{
    public class TurnExecutor
    {
        private readonly RaceMap _map;
        public List<TurnLog> Logs { get; } = new List<TurnLog>();

        public TurnExecutor(RaceMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public void ApplyInstruction(Racer racer, ConcreteInstruction ins, Room room)
        {
            // 先执行动作
            switch (ins.Type)
            {
                case CardType.Move:
                    if (ins.ExecParameter.Effect <=0 || ins.ExecParameter.Effect > 2)
                    {
                        return; //or throw?
                    }
                    MoveForward(racer, ins.ExecParameter.Effect, room);
                    break;
                case CardType.ChangeLane:
                    if (ins.ExecParameter.Effect != -1 && ins.ExecParameter.Effect != 1)
                    {
                        return; //or throw?
                    }
                    ChangeLane(racer, ins.ExecParameter.Effect, room);
                    break;
                case CardType.Repair:
                    Repair(racer, ins.ExecParameter.DiscardedCardIds, room);
                    break;
                default:
                    // Junk 不会在此提交
                    break;
            }
            // 记录日志：每条指令结束后
            Logs.Add(new TurnLog
            {
                PlayerId = racer.Id,
                Instruction = ins,
                SegmentIndex = racer.SegmentIndex,
                LaneIndex = racer.LaneIndex
            });
        }

        private void MoveForward(Racer racer, int steps, Room room)
        {
            for (int i = 0; i < steps; i++)
            {
                int nextSeg = (racer.SegmentIndex + 1) % _map.Segments.Count;
                var seg = _map.Segments[nextSeg];

                // 如果当前 laneIndex 超出下段车道数，则撞墙
                if (racer.LaneIndex >= seg.LaneCount)
                {
                    // 撞墙：两人皆得 Junk
                    AddJunk(racer, 1);
                    return; // 停止本条 Forward
                }

                // 向前迈入下一段
                racer.SegmentIndex = nextSeg;

                // 碰撞检查：同段同道即撞人
                var collided = room.Racers
                    .Where(r => r.Id != racer.Id
                             && r.SegmentIndex == racer.SegmentIndex
                             && r.LaneIndex == racer.LaneIndex)
                    .ToList();

                if (collided.Any())
                {
                    // 主动撞与被撞都得 Junk
                    AddJunk(racer, 1);
                    foreach (var other in collided)
                    {
                        AddJunk(other, 1);
                        // 被撞者后退一段
                        other.SegmentIndex =
                            (other.SegmentIndex - 1 + _map.Segments.Count)
                            % _map.Segments.Count;
                    }
                }
            }
        }

        private void ChangeLane(Racer racer, int delta, Room room)
        {
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
                         && r.LaneIndex == racer.LaneIndex)
                .ToList();
            if (collided.Any())
            {
                AddJunk(racer, 1);
                foreach (var other in collided)
                    AddJunk(other, 1);
            }
        }

        private void AddJunk(Racer racer, int qty)
        {
            for (int i = 0; i < qty; i++)
                racer.Deck.Enqueue(new Card { Type = CardType.Junk });
        }

        private void Repair(Racer racer, List<string> discardedCardId, Room room)
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
            InternalDiscard(racer, discardedCardId, room);
        }

        // dicard cards
        private static void InternalDiscard(Racer racer, List<string> discardedCardId, Room room)
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

        public bool DiscardCards(Racer racer, List<string> discardedCardId, Room room)
        {
            // see if the size is legit
            if (discardedCardId.Count == 0)
                return false;
            // check if the cards are in the hand
            foreach (var cardId in discardedCardId)
            {
                var card = racer.Hand
                    .FirstOrDefault(c => c.Id == cardId);
                if (card == null)
                    return false; // not all in hand
            }
            // discard the cards the user chose
            InternalDiscard(racer, discardedCardId, room);
            return true;
        }
    }
}
