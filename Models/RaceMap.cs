namespace Toko.Models
{
    public class RaceMap
    {
        public int Width { get; set; }
        public int Height { get; set; }
        // two-dimensional array to represent the map: for example, Tiles[0, 0] is the top-left corner
        // and Tiles[Height - 1, Width - 1] is the bottom-right corner
        public TileType[,] Tiles { get; set; }

        public RaceMap(int width, int height)
        {
            Width = width;
            Height = height;
            Tiles = new TileType[height, width];
        }
    }
}
