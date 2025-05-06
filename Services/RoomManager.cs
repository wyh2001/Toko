using System.Collections.Concurrent;
using Toko.Models;

namespace Toko.Services
{
    public class RoomManager
    {
        //private readonly Dictionary<string, Room> _rooms = new();
        private readonly ConcurrentDictionary<string, Room> _rooms = new();

        public (string roomId, Racer host) CreateRoom(
            string? roomName,
            int maxPlayers,
            bool isPrivate,
            string playerName)
        {
            var roomId = Guid.NewGuid().ToString();
            var room = new Room { Id = roomId, Name = roomName, MaxPlayers = maxPlayers, IsPrivate = isPrivate };
            var host = new Racer { Id = Guid.NewGuid().ToString(), PlayerName = playerName,};
            room.Racers.Add(host);
            _rooms.TryAdd(roomId, room);
            return (roomId, host);
        }

        public Room? GetRoom(string id) =>
            _rooms.TryGetValue(id, out var room) ? room : null;

        public (bool success, Racer? racer) JoinRoom(string roomId, string displayedName)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                var racer = new Racer { PlayerName = displayedName };
                room.Racers.Add(racer);
                return (true, racer);
            }
            return (false, null);
        }

        public List<Room> GetAllRooms() => _rooms.Values.ToList();

        public bool SubmitInstructions(string roomId, string playerId, List<InstructionType> instructions)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                var racer = room.Racers.FirstOrDefault(r => r.Id == playerId);
                if (racer != null)
                {
                    //room.SubmittedInstructions[playerId] = instructions;
                    room.SubmittedInstructions.TryAdd(playerId, instructions);
                    return true;
                }

            }
            return false;
        }

        public bool SetMap(string roomId, RaceMap map)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                room.Map = map;
                return true;
            }
            return false;
        }

        public RaceMap? GetMap(string roomId)
        {
            if (_rooms.TryGetValue(roomId, out var room))
                return room.Map;
            return null;
        }

    }
}
