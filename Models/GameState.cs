namespace ColorGame.Models
{
    public class GameState
    {
        public bool IsStarted { get; set; } = false;
        public bool IsOver { get; set; } = false;
        public int CurrentPlayerIndex { get; set; } = 0;
        public int TurnNumber { get; set; } = 0;             // 0 = primer turno (jugador nombra la partida)
        public string PartidaName { get; set; } = "";        // Nombre de la partida actual
        public List<string> UsedColors { get; set; } = new(); // stored lowercase+trimmed
        public string? LoserName { get; set; }
        public string? LosingColor { get; set; }
        public double TotalSeconds { get; set; } = 0;

        // Stats por jugador en la partida activa (key = nombre del jugador)
        public Dictionary<string, PlayerStat> CurrentPlayerStats { get; set; } = new();
    }
}
