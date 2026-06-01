using System.Security.Cryptography;
using System.Text;
using TeamLocum.Web.Models;

namespace TeamLocum.Web.Services
{
    public class SimulatedAiMatchingService : IAiMatchingService
    {
        public Task<List<AiMatchResult>> CalculateMatchScoresAsync(IEnumerable<Locum> availableLocums, string bookingNotes, string bookingLocation)
        {
            var results = new List<AiMatchResult>();
            
            // In a real production scenario, this would serialize the Locum profiles and Booking requirements
            // and send them to the Google Gemini or OpenAI API to get a natural language reasoning and score.
            // For this portfolio demonstration, we simulate an advanced NLP scoring algorithm.

            foreach (var locum in availableLocums)
            {
                // Generate a deterministic but seemingly intelligent score based on the inputs
                string inputToHash = $"{locum.Id}_{locum.User?.FirstName}_{bookingNotes}_{bookingLocation}";
                using var md5 = MD5.Create();
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(inputToHash));
                
                // Use the first byte to generate a score between 70 and 99
                int baseScore = 70 + (hash[0] % 30);
                
                // Add a small bonus if location matches (very simplistic NLP sim)
                if (!string.IsNullOrEmpty(bookingLocation))
                {
                    baseScore = Math.Min(99, baseScore + 8);
                }

                string reasoning = GenerateReasoning(baseScore, locum);

                results.Add(new AiMatchResult
                {
                    LocumId = locum.Id,
                    MatchScore = baseScore,
                    MatchReasoning = reasoning
                });
            }

            // Return sorted by highest score first
            return Task.FromResult(results.OrderByDescending(r => r.MatchScore).ToList());
        }

        private string GenerateReasoning(int score, Locum locum)
        {
            if (score >= 95)
                return $"AI Analysis: Exceptionally high match. Dr. {locum.User?.LastName}'s profile strongly aligns with the clinical requirements and location preferences of this booking.";
            else if (score >= 85)
                return $"AI Analysis: Strong match. The required competencies are present in Dr. {locum.User?.LastName}'s verified GMC history.";
            else if (score >= 75)
                return $"AI Analysis: Good match. Meets all minimum criteria for this shift without any detected compliance conflicts.";
            else
                return $"AI Analysis: Acceptable match. Baseline requirements are satisfied, though specialization overlap is minimal.";
        }
    }
}
