namespace SpecMind.Models
{
    public class AiResult
    {
        public int score { get; set; }
        public int completeness { get; set; }
        public int clarity { get; set; }
        public string risk { get; set; } = "Не определён";

        public int strategicRelevance { get; set; }
        public int goalAndTasks { get; set; }
        public int scientificNovelty { get; set; }
        public int practicalApplicability { get; set; }
        public int expectedResults { get; set; }
        public int socioEconomicEffect { get; set; }
        public int feasibility { get; set; }

        public List<string> problems { get; set; } = new();
        public List<string> recommendations { get; set; } = new();

        public string summary { get; set; } = string.Empty;
        public string improvedVersion { get; set; } = string.Empty;
    }
}