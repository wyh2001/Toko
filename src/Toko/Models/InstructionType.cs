namespace Toko.Models
{
    public enum InstructionType
    {
        Move,        // Parameter = number of steps
        ChangeLane,   // Parameter = -1 or +1
        Repair,      // Discard two junk carda
        Discard,    // Discard some cards
        NoAction,
    }
}
