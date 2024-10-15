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
                if (!File.Exists(configFilePath))
                {
                    throw new FileNotFoundException($"The configuration file '{configFilePath}' was not found.");
                }

                var configJson = File.ReadAllText(configFilePath);

                // Stricter deserialization options
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Disallow, // Disallow comments
                    AllowTrailingCommas = false // Disallow trailing commas
                };

                return JsonSerializer.Deserialize<AppConfig>(configJson, options);
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"Failed to load configuration: {ex.Message}");
                throw;
            }
        }
    }
}