namespace Toko.Models
{
    public enum CardType
    {
        //Forward1, Forward2,
        //ChangeLeft, ChangeRight,
        //ChangeLeft_Forward1, ChangeRight_Forward1,
        //Forward1_ChangeLeft, Forward1_ChangeRight,
        //Junk, Repair
        Move,        // 抽象的“前进”卡
        ChangeLane,  // 抽象的“变道”卡
        Junk,
        Repair
    }
}
