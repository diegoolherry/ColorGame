using System.Linq;
using System.Collections.Generic;

namespace ColorGame.Models
{
    public class Room
    {
        public string Code { get; set; } = "";
        public List<Player> Players { get; set; } = new();
        public GameState Game { get; set; } = new();
        public List<Partida> History { get; set; } = new();          // Historial de partidas terminadas en esta ronda
        public HashSet<string> BannedColors { get; set; } = new();   // Colores usados en la partida anterior (normalizados)

        public Player? Admin => Players.FirstOrDefault(p => p.Role == "Admin");
        public List<Player> GamePlayers => Players.ToList();
    }
}
