namespace Toko.Models
{
    public class ExecParameter
    {
        public int Effect { get; set; } = -1;
        public List<string> DiscardedCardIds { get; set; } = [];
    }
}
