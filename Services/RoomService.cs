using System;
using System.Collections.Generic;
using System.Linq;
using ColorGame.Models;

namespace ColorGame.Services
{
    public class RoomService
    {
        private readonly Dictionary<string, Room> _rooms = new();
        private readonly object _lock = new();
        private readonly Random _random = new();

        public Room CreateRoom(string adminName, string connectionId)
        {
            lock (_lock)
            {
                string code;
                do
                {
                    code = _random.Next(100000, 999999).ToString();
                } while (_rooms.ContainsKey(code));

                var room = new Room { Code = code };
                room.Players.Add(new Player
                {
                    ConnectionId = connectionId,
                    Name = adminName,
                    Role = "Admin"
                });

                _rooms[code] = room;
                return room;
            }
        }

        public (Room? room, string? error) JoinRoom(string code, string playerName, string connectionId)
        {
            lock (_lock)
            {
                if (!_rooms.TryGetValue(code, out var room))
                {
                    return (null, "La sala no existe");
                }

                if (room.Game.IsStarted)
                {
                    return (null, "El juego ya ha comenzado");
                }

                if (room.Players.Any(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
                {
                    return (null, "El nombre ya está en uso en esta sala");
                }

                room.Players.Add(new Player
                {
                    ConnectionId = connectionId,
                    Name = playerName,
                    Role = "Player"
                });

                return (room, null);
            }
        }

        public Room? GetRoom(string code)
        {
            lock (_lock)
            {
                _rooms.TryGetValue(code, out var room);
                return room;
            }
        }

        public string? GetRoomCodeByConnection(string connectionId)
        {
            lock (_lock)
            {
                return _rooms.Values
                    .FirstOrDefault(r => r.Players.Any(p => p.ConnectionId == connectionId))
                    ?.Code;
            }
        }

        public void RemovePlayerByConnection(string connectionId)
        {
            lock (_lock)
            {
                var room = _rooms.Values.FirstOrDefault(r => r.Players.Any(p => p.ConnectionId == connectionId));
                if (room != null)
                {
                    var player = room.Players.First(p => p.ConnectionId == connectionId);
                    room.Players.Remove(player);

                    // Optional: If room is empty, remove it. Admin leaving logic might be handled in Hub too.
                    if (room.Players.Count == 0)
                    {
                        _rooms.Remove(room.Code);
                    }
                }
            }
        }

        public void ResetGame(string code)
        {
            lock (_lock)
            {
                if (_rooms.TryGetValue(code, out var room))
                {
                    room.Game = new GameState();
                    foreach (var player in room.Players)
                    {
                        player.AccumulatedSeconds = 0;
                    }
                }
            }
        }
        
        public void RemoveRoom(string code)
        {
            lock (_lock)
            {
                _rooms.Remove(code);
            }
        }
    }
}
