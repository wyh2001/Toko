using Microsoft.VisualBasic;

namespace Toko.Models
{
    public class Card
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public CardType Type { get; set; }
    }

}
