namespace ColorGame.Models
{
    public class PlayerStat
    {
        public string Name { get; set; } = "";
        public double AccumulatedSeconds { get; set; } = 0;
        public List<string> Colors { get; set; } = new();
    }
}
