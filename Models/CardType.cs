namespace Toko.Models
{
    public enum CardType
    {
        Move,        // 抽象的“前进”卡
        ChangeLane,  // 抽象的“变道”卡
        Junk,
        Repair
    }
}
