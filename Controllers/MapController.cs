using Microsoft.AspNetCore.Mvc;
using Toko.Models;
using Toko.Services;

namespace Toko.Controllers
{
    [ApiController]
    [Route("api/room/{roomId}/map")]
    public class MapController : ControllerBase
    {
        private readonly RoomManager _roomManager;

        public MapController(RoomManager roomManager)
        {
            _roomManager = roomManager;
        }

        [HttpPost]
        public IActionResult InitMap(string roomId, [FromBody] RaceMapDto dto)
        {
            var map = new RaceMap(dto.Width, dto.Height);
            foreach (var tile in dto.Tiles)
            {
                if (tile.X >= 0 && tile.X < dto.Width && tile.Y >= 0 && tile.Y < dto.Height)
                    map.Tiles[tile.Y, tile.X] = tile.Type;
            }

            if (!_roomManager.SetMap(roomId, map))
                return NotFound("Room not found.");

            return Ok(new { message = "Map initialized." });
        }

        [HttpGet]
        public IActionResult GetMap(string roomId)
        {
            var map = _roomManager.GetMap(roomId);
            if (map == null)
                return NotFound("Map not set or room not found.");

            var dto = new RaceMapDto
            {
                Width = map.Width,
                Height = map.Height,
                Tiles = Enumerable.Range(0, map.Height)
                    .SelectMany(y => Enumerable.Range(0, map.Width)
                        .Select(x => new MapTileDto
                        {
                            X = x,
                            Y = y,
                            Type = map.Tiles[y, x]
                        }))
                    .ToList()
            };
            return Ok(dto);
        }

        public class RaceMapDto
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public List<MapTileDto> Tiles { get; set; } = new();
        }

        public class MapTileDto
        {
            public int X { get; set; }
            public int Y { get; set; }
            public TileType Type { get; set; }
        }
    }
}
