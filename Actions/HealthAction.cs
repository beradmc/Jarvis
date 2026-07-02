using System;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using JarvisCSharp.Services;

namespace JarvisCSharp.Actions
{
    /// <summary>
    /// Health action for retrieving iPhone health data from iCloud Drive.
    /// Implements Requirements 11, 12, 26 - Health Data Integration
    /// </summary>
    public class HealthAction : IAction
    {
        private readonly HealthDataService _healthDataService;

        public string Name => "get_health_data";
        public string Description => "iPhone sağlık verilerini iCloud Drive'dan okur ve özetler.";

        public HealthAction()
        {
            _healthDataService = new HealthDataService();
        }

        /// <summary>
        /// Execute health data query with simple string payload.
        /// Payload format: plain query string (e.g., "bugün", "heart rate", "all")
        /// </summary>
        public Task<string> ExecuteAsync(string payload)
        {
            try
            {
                // Parse payload - expect simple query string
                string query = ParsePayload(payload);
                
                // Call HealthDataService to get health data
                string result = _healthDataService.GetHealthData(query);
                
                // Log result summary
                Logger.Information($"[Health] Query: '{query}' - Result: {result[..Math.Min(120, result.Length)]}...");
                
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "HealthAction failed");
                return Task.FromResult($"Hata: Sağlık verisi alınamadı — {ex.Message}");
            }
        }

        /// <summary>
        /// Parse the incoming payload to extract the query parameter.
        /// Supports both simple string format and potential JSON format for future extensibility.
        /// </summary>
        private string ParsePayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return "all";
            }

            // Simple string payload - just return trimmed value
            string trimmed = payload.Trim();
            
            // If it starts with "get:" prefix (similar to CalendarAction pattern), strip it
            if (trimmed.StartsWith("get:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[4..].Trim();
            }

            // Otherwise return as-is
            return trimmed;
        }
    }
}
