namespace ColorGame.Models
{
    public class Partida
    {
        public int Number { get; set; }                         // Nro de partida en la ronda (1, 2, 3...)
        public string Name { get; set; } = "";                  // Nombre dado por el primer jugador
        public double TotalSeconds { get; set; } = 0;
        public List<string> UsedColors { get; set; } = new();   // Colores usados en esta partida
        public string LoserName { get; set; } = "";
        public string LosingColor { get; set; } = "";
        public List<PlayerStat> PlayerStats { get; set; } = new();
    }
}
