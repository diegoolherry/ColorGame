using Microsoft.AspNetCore.SignalR;
using ColorGame.Models;
using ColorGame.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ColorGame.Hubs
{
    public class GameHub : Hub
    {
        private readonly RoomService _roomService;

        public GameHub(RoomService roomService)
        {
            _roomService = roomService;
        }

        public async Task CreateRoom(string adminName)
        {
            var room = _roomService.CreateRoom(adminName, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
            await Clients.Caller.SendAsync("RoomCreated", room.Code);
        }

        public async Task JoinRoom(string roomCode, string playerName)
        {
            var (room, error) = _roomService.JoinRoom(roomCode, playerName, Context.ConnectionId);
            
            if (error != null)
            {
                await Clients.Caller.SendAsync("JoinError", error);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
            
            var playerNames = room!.Players.Select(p => p.Name).ToList();
            await Clients.Caller.SendAsync("JoinedRoom", roomCode, playerNames);
            await Clients.GroupExcept(roomCode, Context.ConnectionId).SendAsync("PlayerJoined", playerName, playerNames);
        }

        public async Task StartGame(string roomCode)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room == null || room.Admin?.ConnectionId != Context.ConnectionId) return;

            var gamePlayers = room.GamePlayers;
            if (gamePlayers.Count < 2)
            {
                await Clients.Caller.SendAsync("StartError", "Se necesitan al menos 2 jugadores");
                return;
            }

            lock (room)
            {
                // Shuffle player order randomly
                var rng = new Random();
                var players = room.Players;
                for (int i = players.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (players[i], players[j]) = (players[j], players[i]);
                }

                room.Game.IsStarted = true;
                room.Game.CurrentPlayerIndex = 0;
            }

            var shuffledGamePlayers = room.GamePlayers;
            var allNames = room.Players.Select(p => p.Name).ToList();
            await Clients.Group(roomCode).SendAsync("GameStarted", shuffledGamePlayers[0].Name, allNames);
        }

        public async Task SubmitColor(string roomCode, string color, double elapsedSeconds)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room == null || !room.Game.IsStarted || room.Game.IsOver) return;

            bool isGameOver = false;
            string loserName = "";
            string losingColor = "";
            double totalSeconds = 0;
            List<object>? scores = null;
            
            string nextPlayerName = "";
            string currentPlayerName = "";

            lock (room)
            {
                var gamePlayers = room.GamePlayers;
                if (gamePlayers.Count == 0 || room.Game.CurrentPlayerIndex >= gamePlayers.Count) return;

                var currentPlayer = gamePlayers[room.Game.CurrentPlayerIndex];
                if (currentPlayer.ConnectionId != Context.ConnectionId) return; // Not their turn

                string normalizedColor = color.Trim().ToLower();

                currentPlayer.AccumulatedSeconds += elapsedSeconds;
                room.Game.TotalSeconds += elapsedSeconds;

                if (room.Game.UsedColors.Contains(normalizedColor))
                {
                    // Game Over — also record the losing (repeated) color
                    currentPlayer.Colors.Add(color);

                    room.Game.IsOver = true;
                    room.Game.LoserName = currentPlayer.Name;
                    room.Game.LosingColor = color; // original

                    isGameOver = true;
                    loserName = room.Game.LoserName;
                    losingColor = room.Game.LosingColor;
                    totalSeconds = room.Game.TotalSeconds;
                    scores = gamePlayers
                        .OrderBy(p => p.AccumulatedSeconds)
                        .Select(p => (object)new { Name = p.Name, AccumulatedSeconds = p.AccumulatedSeconds, Colors = p.Colors })
                        .ToList();
                }
                else
                {
                    // Continue — record the valid color
                    currentPlayer.Colors.Add(color);
                    room.Game.UsedColors.Add(normalizedColor);
                    currentPlayerName = currentPlayer.Name;
                    room.Game.CurrentPlayerIndex = (room.Game.CurrentPlayerIndex + 1) % gamePlayers.Count;
                    var nextPlayer = gamePlayers[room.Game.CurrentPlayerIndex];
                    nextPlayerName = nextPlayer.Name;
                }
            }

            if (isGameOver)
            {
                await Clients.Group(roomCode).SendAsync("GameOver", loserName, losingColor, totalSeconds, scores);
            }
            else if (!string.IsNullOrEmpty(nextPlayerName))
            {
                await Clients.Group(roomCode).SendAsync("NextTurn", nextPlayerName, color, currentPlayerName);
            }
        }

        public async Task ResetGame(string roomCode)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room == null || room.Admin?.ConnectionId != Context.ConnectionId) return;

            // Clear per-player colors before resetting
            foreach (var player in room.Players)
            {
                player.Colors.Clear();
            }

            _roomService.ResetGame(roomCode);
            var names = room.Players.Select(p => p.Name).ToList();
            await Clients.Group(roomCode).SendAsync("GameReset", names);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var roomCode = _roomService.GetRoomCodeByConnection(Context.ConnectionId);
            if (roomCode != null)
            {
                var room = _roomService.GetRoom(roomCode);
                if (room != null)
                {
                    bool isAdmin = room.Admin?.ConnectionId == Context.ConnectionId;
                    var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                    string? leavingPlayerName = player?.Name;
                    
                    int indexBeforeRemoval = -1;
                    if (player != null && player.Role == "Player" && room.Game.IsStarted && !room.Game.IsOver)
                    {
                         indexBeforeRemoval = room.GamePlayers.IndexOf(player);
                    }

                    _roomService.RemovePlayerByConnection(Context.ConnectionId);

                    if (isAdmin)
                    {
                        _roomService.RemoveRoom(roomCode);
                        await Clients.Group(roomCode).SendAsync("AdminLeft");
                    }
                    else if (leavingPlayerName != null)
                    {
                        var names = room.Players.Select(p => p.Name).ToList();
                        await Clients.Group(roomCode).SendAsync("PlayerLeft", leavingPlayerName, names);

                        // If game in progress and this player left
                        if (room.Game.IsStarted && !room.Game.IsOver)
                        {
                            var gamePlayers = room.GamePlayers;
                            if (gamePlayers.Count < 2)
                            {
                                // End game if less than 2 players remain
                                room.Game.IsOver = true;
                                room.Game.LoserName = "Nadie (abandonaron)";
                                room.Game.LosingColor = "N/A";
                                
                                var scores = gamePlayers
                                    .OrderBy(p => p.AccumulatedSeconds)
                                    .Select(p => new { Name = p.Name, AccumulatedSeconds = p.AccumulatedSeconds })
                                    .ToList();

                                await Clients.Group(roomCode).SendAsync("GameOver", room.Game.LoserName, room.Game.LosingColor, room.Game.TotalSeconds, scores);
                            }
                            else if (indexBeforeRemoval != -1)
                            {
                                if (room.Game.CurrentPlayerIndex == indexBeforeRemoval)
                                {
                                    // It was their turn. Turn shifts to current index modulo count
                                    room.Game.CurrentPlayerIndex = room.Game.CurrentPlayerIndex % gamePlayers.Count;
                                    var nextPlayer = gamePlayers[room.Game.CurrentPlayerIndex];
                                    await Clients.Group(roomCode).SendAsync("NextTurn", nextPlayer.Name, "Abandono turno", leavingPlayerName);
                                }
                                else if (indexBeforeRemoval < room.Game.CurrentPlayerIndex)
                                {
                                    room.Game.CurrentPlayerIndex--;
                                }
                            }
                        }
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
