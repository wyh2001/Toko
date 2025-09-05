using Toko.Models;
using Toko.Shared.Models;

namespace Toko.Services
{
    public class CardHelper
    {
        /// <summary>
        /// Initialize a standard deck: populate with required card types
        /// </summary>
        public static void InitializeDeck(Racer racer)
        {
            // Clear old cards
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

            // Finally shuffle randomly
            // Fisher–Yates shuffle
            //var rnd = new Random();
            var all = racer.Deck.ToList();
            ShuffleUtils.Shuffle(all);
            racer.Deck.Clear();
            foreach (var card in all)
                racer.Deck.Enqueue(card);
        }

        /// <summary>
        /// Internal: Actually draw from deck without free-slot check, return drawn cards
        /// </summary>
        public static List<Card> DrawCardsInternal(Racer racer, int count)
        {
            var drawn = new List<Card>();
            for (int i = 0; i < count; i++)
            {
                // If deck is empty, recycle discard pile back into deck
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
            //private static readonly Random _random = new Random(); // Avoid creating new Random instance each time

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
