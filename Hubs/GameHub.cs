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

        // ─────────────────────────────────────────────────────────────────────
        // ROOM MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────────────────
        // GAME FLOW
        // ─────────────────────────────────────────────────────────────────────

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

            List<string> bannedColors;
            string firstPlayer;
            List<string> allNames;
            int partidaNumber;

            lock (room)
            {
                // Shuffle player order randomly (Fisher-Yates)
                var rng = new Random();
                var players = room.Players;
                for (int i = players.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (players[i], players[j]) = (players[j], players[i]);
                }

                room.Game.IsStarted = true;
                room.Game.CurrentPlayerIndex = 0;
                room.Game.TurnNumber = 0;

                // Initialize player stats for this partida
                room.Game.CurrentPlayerStats = new Dictionary<string, PlayerStat>();
                foreach (var p in room.GamePlayers)
                {
                    room.Game.CurrentPlayerStats[p.Name] = new PlayerStat { Name = p.Name };
                }

                bannedColors = room.BannedColors.ToList();
                firstPlayer = room.GamePlayers[0].Name;
                allNames = room.Players.Select(p => p.Name).ToList();
                partidaNumber = room.History.Count + 1;
            }

            await Clients.Group(roomCode).SendAsync("GameStarted", firstPlayer, allNames, bannedColors, partidaNumber);
        }

        public async Task SubmitColor(string roomCode, string color, double elapsedSeconds, string? partidaName)
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
            bool isFirstTurn = false;
            string resolvedPartidaName = "";

            lock (room)
            {
                var gamePlayers = room.GamePlayers;
                if (gamePlayers.Count == 0 || room.Game.CurrentPlayerIndex >= gamePlayers.Count) return;

                var currentPlayer = gamePlayers[room.Game.CurrentPlayerIndex];
                if (currentPlayer.ConnectionId != Context.ConnectionId) return;

                string normalizedColor = color.Trim().ToLower();

                // On first turn, record the partida name
                if (room.Game.TurnNumber == 0)
                {
                    room.Game.PartidaName = string.IsNullOrWhiteSpace(partidaName)
                        ? $"Partida {room.History.Count + 1}"
                        : partidaName.Trim();
                    isFirstTurn = true;
                }
                resolvedPartidaName = room.Game.PartidaName;

                // Get or create player stat
                if (!room.Game.CurrentPlayerStats.TryGetValue(currentPlayer.Name, out var stat))
                {
                    stat = new PlayerStat { Name = currentPlayer.Name };
                    room.Game.CurrentPlayerStats[currentPlayer.Name] = stat;
                }

                stat.AccumulatedSeconds += elapsedSeconds;
                room.Game.TotalSeconds += elapsedSeconds;

                // Check both used colors AND banned colors
                bool isUsed   = room.Game.UsedColors.Contains(normalizedColor);
                bool isBanned = room.BannedColors.Contains(normalizedColor);

                if (isUsed || isBanned)
                {
                    // Record the losing color and finalize
                    stat.Colors.Add(color);

                    room.Game.IsOver = true;
                    room.Game.LoserName = currentPlayer.Name;
                    room.Game.LosingColor = color;

                    string reason = isBanned ? "prohibido (partida anterior)" : "repetido";

                    isGameOver = true;
                    loserName = currentPlayer.Name;
                    losingColor = color;
                    totalSeconds = room.Game.TotalSeconds;
                    scores = gamePlayers
                        .Select(p =>
                        {
                            var s = room.Game.CurrentPlayerStats.TryGetValue(p.Name, out var ps)
                                ? ps
                                : new PlayerStat { Name = p.Name };
                            return (object)new
                            {
                                Name = s.Name,
                                AccumulatedSeconds = s.AccumulatedSeconds,
                                Colors = s.Colors,
                                IsLoser = p.Name == currentPlayer.Name
                            };
                        })
                        .OrderBy(x => ((dynamic)x).AccumulatedSeconds)
                        .ToList();

                    // Save completed partida to room history
                    var partida = new Partida
                    {
                        Number = room.History.Count + 1,
                        Name = room.Game.PartidaName,
                        TotalSeconds = room.Game.TotalSeconds,
                        UsedColors = new List<string>(room.Game.UsedColors),
                        LoserName = currentPlayer.Name,
                        LosingColor = color,
                        PlayerStats = room.Game.CurrentPlayerStats.Values.ToList()
                    };
                    room.History.Add(partida);
                }
                else
                {
                    // Valid color — record it
                    stat.Colors.Add(color);
                    room.Game.UsedColors.Add(normalizedColor);
                    room.Game.TurnNumber++;
                    currentPlayerName = currentPlayer.Name;
                    room.Game.CurrentPlayerIndex = (room.Game.CurrentPlayerIndex + 1) % gamePlayers.Count;
                    nextPlayerName = gamePlayers[room.Game.CurrentPlayerIndex].Name;
                }
            }

            if (isGameOver)
            {
                await Clients.Group(roomCode).SendAsync("GameOver", loserName, losingColor, totalSeconds, scores, resolvedPartidaName);
            }
            else if (!string.IsNullOrEmpty(nextPlayerName))
            {
                await Clients.Group(roomCode).SendAsync("NextTurn", nextPlayerName, color, currentPlayerName);
            }
        }

        /// <summary>
        /// Admin starts the next Partida within the same Ronda.
        /// Preserves history, bans old colors, resets game state.
        /// </summary>
        public async Task NextPartida(string roomCode)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room == null || room.Admin?.ConnectionId != Context.ConnectionId) return;

            _roomService.StartNextPartida(roomCode);

            var names = room.Players.Select(p => p.Name).ToList();
            var bannedColors = room.BannedColors.ToList();
            await Clients.Group(roomCode).SendAsync("NextPartidaReady", names, bannedColors);
        }

        /// <summary>
        /// Admin ends the entire round and shows the Leaderboard.
        /// </summary>
        public async Task EndRound(string roomCode)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room == null || room.Admin?.ConnectionId != Context.ConnectionId) return;

            // Order history: longest game first (descending by TotalSeconds)
            var leaderboard = room.History
                .OrderByDescending(p => p.TotalSeconds)
                .Select((p, i) => (object)new
                {
                    Position  = i + 1,
                    Name      = p.Name,
                    TotalSeconds = p.TotalSeconds,
                    LoserName = p.LoserName,
                    LosingColor = p.LosingColor,
                    Number    = p.Number
                })
                .ToList();

            await Clients.Group(roomCode).SendAsync("RoundEnded", leaderboard);
        }

        /// <summary>
        /// Admin fully resets — clears history and goes back to lobby.
        /// </summary>
        public async Task ResetGame(string roomCode)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room == null || room.Admin?.ConnectionId != Context.ConnectionId) return;

            _roomService.ResetRoom(roomCode);
            var names = room.Players.Select(p => p.Name).ToList();
            await Clients.Group(roomCode).SendAsync("GameReset", names);
        }

        // ─────────────────────────────────────────────────────────────────────
        // DISCONNECTION
        // ─────────────────────────────────────────────────────────────────────

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

                        if (room.Game.IsStarted && !room.Game.IsOver)
                        {
                            var gamePlayers = room.GamePlayers;
                            if (gamePlayers.Count < 2)
                            {
                                room.Game.IsOver = true;
                                room.Game.LoserName = "Nadie (abandonaron)";
                                room.Game.LosingColor = "N/A";

                                var scores = gamePlayers
                                    .Select(p =>
                                    {
                                        var s = room.Game.CurrentPlayerStats.TryGetValue(p.Name, out var ps)
                                            ? ps : new PlayerStat { Name = p.Name };
                                        return (object)new { Name = s.Name, AccumulatedSeconds = s.AccumulatedSeconds, Colors = s.Colors, IsLoser = false };
                                    })
                                    .OrderBy(x => ((dynamic)x).AccumulatedSeconds)
                                    .ToList();

                                await Clients.Group(roomCode).SendAsync("GameOver", room.Game.LoserName, room.Game.LosingColor, room.Game.TotalSeconds, scores, room.Game.PartidaName);
                            }
                            else if (indexBeforeRemoval != -1)
                            {
                                if (room.Game.CurrentPlayerIndex == indexBeforeRemoval)
                                {
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
