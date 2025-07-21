using Toko.Models;
using Toko.Shared.Models;

namespace Toko.Services
{
    public class CardHelper
    {
        /// <summary>
        /// 初始化一副标准牌堆：根据需要填充不同类型卡
        /// </summary>
        public static void InitializeDeck(Racer racer)
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

            Add(CardType.ChangeLane, 2);
            Add(CardType.Repair, 2);
            Add(CardType.ShiftGear, 4); // Combined gear shift card

            // 最后随机洗牌
            // Fisher–Yates shuffle
            //var rnd = new Random();
            var all = racer.Deck.ToList();
            ShuffleUtils.Shuffle(all);
            racer.Deck.Clear();
            foreach (var card in all)
                racer.Deck.Enqueue(card);
        }

        /// <summary>
        /// Internal: 实际从 Deck 抽卡，不作空槽检查，返回抽到的卡
        /// </summary>
        public static List<Card> DrawCardsInternal(Racer racer, int count)
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
            
            // Adjust gear after drawing cards in case junk cards were drawn
            AdjustGearForJunkCards(racer);
            
            return drawn;
        }

        /// <summary>
        /// Adjusts racer's gear to comply with junk card limitations
        /// </summary>
        public static void AdjustGearForJunkCards(Racer racer)
        {
            int junkCardCount = racer.Hand.Count(card => card.Type == Shared.Models.CardType.Junk);
            int maxAllowedGear = 6 - junkCardCount; // Each junk card reduces max gear by 1
            
            if (racer.Gear > maxAllowedGear)
            {
                racer.Gear = maxAllowedGear;
            }
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
    }
}
