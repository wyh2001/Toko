namespace Toko.Models
{
    public enum InstructionType
    {
        Move,        // Parameter = 步数
        ChangeLane,   // Parameter = -1 or +1
        Repair,      // Discard two junk carda
        Discard,    // Discard some cards
        NoAction,    
    }
}
