using System.Linq;
using System.Collections.Generic;

namespace ColorGame.Models
{
    public class Room
    {
        public string Code { get; set; } = "";
        public List<Player> Players { get; set; } = new();
        public GameState Game { get; set; } = new();
        public Player? Admin => Players.FirstOrDefault(p => p.Role == "Admin");
        public List<Player> GamePlayers => Players.ToList();
    }
}
