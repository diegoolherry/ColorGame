using Microsoft.AspNetCore.SignalR;
using ColorGame.Hubs;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace ColorGame.Services
{
    public class TurnTimerService
    {
        private readonly IHubContext<GameHub> _hubContext;
        private readonly RoomService _roomService;

        public TurnTimerService(IHubContext<GameHub> hubContext, RoomService roomService)
        {
            _hubContext = hubContext;
            _roomService = roomService;
        }
        public void CancelTimer(string roomCode)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room != null)
            {
                lock (room)
                {
                    if (room.Game.TurnTimerTokenSource != null)
                    {
                        room.Game.TurnTimerTokenSource.Cancel();
                        room.Game.TurnTimerTokenSource.Dispose();
                        room.Game.TurnTimerTokenSource = null;
                    }
                }
            }
        }

        public void StartTurnTimer(string roomCode, string expectedPlayerName)
        {
            var room = _roomService.GetRoom(roomCode);
            if (room != null)
            {
                lock (room)
                {
                    CancelTimer(roomCode); // Just in case
                    var cts = new CancellationTokenSource();
                    room.Game.TurnTimerTokenSource = cts;
                    _ = WaitForTimeoutAsync(roomCode, expectedPlayerName, cts.Token);
                }
            }
        }

        private async Task WaitForTimeoutAsync(string roomCode, string expectedPlayerName, CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
            catch (TaskCanceledException)
            {
                return; // Timer was canceled
            }

            var room = _roomService.GetRoom(roomCode);
            if (room == null) return;

            bool isGameOver = false;
            string loserName = "Tiempo agotado";
            string losingColor = "Tiempo agotado";
            double totalSeconds = 0;
            object? scores = null;

            lock (room)
            {
                if (!room.Game.IsStarted || room.Game.IsOver) return;

                var gamePlayers = room.GamePlayers;
                if (gamePlayers.Count == 0 || room.Game.CurrentPlayerIndex >= gamePlayers.Count) return;

                var currentPlayer = gamePlayers[room.Game.CurrentPlayerIndex];
                if (currentPlayer.Name != expectedPlayerName) return;

                // Time is up for this player
                room.Game.IsOver = true;
                room.Game.LoserName = currentPlayer.Name;
                room.Game.LosingColor = "Tiempo agotado";

                // Penalize with the 10 seconds they took
                currentPlayer.AccumulatedSeconds += 10;
                room.Game.TotalSeconds += 10;

                isGameOver = true;
                loserName = room.Game.LoserName;
                losingColor = room.Game.LosingColor;
                totalSeconds = room.Game.TotalSeconds;
                scores = gamePlayers
                    .OrderBy(p => p.AccumulatedSeconds)
                    .Select(p => new { name = p.Name, accumulatedSeconds = p.AccumulatedSeconds })
                    .ToList();
            }

            if (isGameOver)
            {
                await _hubContext.Clients.Group(roomCode).SendAsync("GameOver", loserName, losingColor, totalSeconds, scores);
            }
        }
    }
}
