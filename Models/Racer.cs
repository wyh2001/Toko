public enum Direction { Up, Right, Down, Left }

namespace Toko.Models
{
    public class Racer
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string PlayerName { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Speed { get; set; } = 0;
        public Direction Facing { get; set; } = Direction.Up;
    }
}
