using GitSiteSyncer.Models;
using System.Text.Json;

namespace GitSiteSyncer.Utilities
{
    public class ConfigLoader
    {
        public static AppConfig LoadConfig(string configFilePath)
        {
            try
            {
                var configJson = File.ReadAllText(configFilePath);
                return JsonSerializer.Deserialize<AppConfig>(configJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load configuration: {ex.Message}");
                throw;
            }
        }
    }
}