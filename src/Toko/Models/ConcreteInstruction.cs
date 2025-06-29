namespace Toko.Models
{
    public class ConcreteInstruction
    {
        public CardType Type { get; set; }
        public ExecParameter ExecParameter { get; set; } = new ExecParameter();
        //public int Parameter { get; set; } // for Move and ChangeLane
        //public string? DiscardedCardId { get; set; } // for Discard/Repair
        //public List<string>? DiscardedCardIds { get; set; } // for Discard/Repair

    }
}
