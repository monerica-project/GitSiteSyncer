using GitSiteSyncer.Utilities;
using System.Text.Json;

namespace GitSiteSyncer.Tests.UtilitiesTests
{
    public class ConfigLoaderTests
    {
        [Fact]
        public void LoadConfig_ValidConfigFile_ShouldReturnAppConfig()
        {
            // Arrange
            var configFilePath = "validConfig.json";
            var configJson = @"{
            ""GitDirectory"": ""/path/to/repo"",
            ""LockFileDirectory"": ""/path/to/lock"",
            ""GitCredentials"": {
                ""Username"": ""user123"",
                ""Password"": ""password123""
            },
            ""SitemapUrl"": ""https://example.com/sitemap.xml"",
            ""DaysToConsider"": 30,
            ""AppHostDomain"": ""app.example.com"",
            ""NoAppHostDomain"": ""no-app.example.com""
        }";
            File.WriteAllText(configFilePath, configJson);

            // Act
            var config = ConfigLoader.LoadConfig(configFilePath);

            // Assert
            Assert.NotNull(config);
            Assert.Equal("/path/to/repo", config.GitDirectory);
            Assert.Equal("/path/to/lock", config.LockFileDirectory);
            Assert.NotNull(config.GitCredentials);
            Assert.Equal("user123", config.GitCredentials.Username);
            Assert.Equal("password123", config.GitCredentials.Password);
            Assert.Equal("https://example.com/sitemap.xml", config.SitemapUrl);
            Assert.Equal(30, config.DaysToConsider);
            Assert.Equal("app.example.com", config.AppHostDomain);
            Assert.Equal("no-app.example.com", config.NoAppHostDomain);

            // Cleanup
            File.Delete(configFilePath);
        }

        [Fact]
        public void LoadConfig_InvalidJson_ShouldThrowJsonException()
        {
            // Arrange
            var configFilePath = "invalidConfig.json";
            var invalidJson = @"{
            ""GitDirectory"": ""/path/to/repo"",
            ""LockFileDirectory"": ""/path/to/lock"",
            ""GitCredentials"": {
                ""Username"": ""user123"",
                ""Password"": ""password123""
            }, // Missing closing brace
        }";
            File.WriteAllText(configFilePath, invalidJson);

            // Act & Assert
            Assert.Throws<JsonException>(() => ConfigLoader.LoadConfig(configFilePath));

            // Cleanup
            File.Delete(configFilePath);
        }

        [Fact]
        public void LoadConfig_NonExistentFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var configFilePath = "nonExistentConfig.json";

            // Ensure the file does not exist before running the test
            if (File.Exists(configFilePath))
            {
                File.Delete(configFilePath); // Clean up if it exists
            }

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() => ConfigLoader.LoadConfig(configFilePath));
        }

        [Fact]
        public void LoadConfig_EmptyFile_ShouldThrowJsonException()
        {
            // Arrange
            var configFilePath = "emptyConfig.json";
            File.WriteAllText(configFilePath, ""); // Empty file

            // Act & Assert
            Assert.Throws<JsonException>(() => ConfigLoader.LoadConfig(configFilePath));

            // Cleanup
            File.Delete(configFilePath);
        }
    }
}