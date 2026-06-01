using TeamLocum.Web.Models;

namespace TeamLocum.Web.Services
{
    public class AiMatchResult
    {
        public int LocumId { get; set; }
        public int MatchScore { get; set; }
        public string MatchReasoning { get; set; } = string.Empty;
    }

    public interface IAiMatchingService
    {
        Task<List<AiMatchResult>> CalculateMatchScoresAsync(IEnumerable<Locum> availableLocums, string bookingNotes, string bookingLocation);
    }
}
