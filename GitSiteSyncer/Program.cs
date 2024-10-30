using GitSiteSyncer.Models;
using GitSiteSyncer.Utilities;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Running...");
        string appRootDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string configFilePath = Path.Combine(appRootDirectory, "appsettings.json");

        AppConfig config;
        try
        {
            config = ConfigLoader.LoadConfig(configFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
            return;
        }

        // Ensure LockFileDirectory is set and exists
        if (string.IsNullOrEmpty(config.LockFileDirectory))
        {
            Console.WriteLine("LockFileDirectory not specified in config.");
            return;
        }
        if (!Directory.Exists(config.LockFileDirectory))
        {
            Directory.CreateDirectory(config.LockFileDirectory);
        }

        string lockFilePath = Path.Combine(config.LockFileDirectory, "app.lock");

        using (LockHelper lockHelper = new LockHelper(lockFilePath))
        {
            if (!lockHelper.TryAcquireLock())
            {
                Console.WriteLine("Another instance is running. Exiting...");
                return;
            }

            try
            {
                GitHelper gitHelper = new GitHelper(config.GitDirectory, config.GitCredentials);

                Console.WriteLine("Synchronizing with remote repository...");
                gitHelper.ForceSyncRepo();

                SitemapReader sitemapReader = new SitemapReader();
                ContentRewriter rewriter = new ContentRewriter(config.AppHostDomain, config.NoAppHostDomain);
                FileDownloader downloader = new FileDownloader(rewriter);

                Console.WriteLine("Fetching sitemap URLs...");
                var urls = await sitemapReader.GetUrlsAsync(config.SitemapUrl, config.DaysToConsider);

                // Download and save the sitemap file
                Console.WriteLine($"Downloading sitemap from {config.SitemapUrl}...");
                string sitemapFilePath = await DownloadSitemapAsync(config.SitemapUrl, config.GitDirectory);
                Console.WriteLine($"Sitemap saved to: {sitemapFilePath}");

                // **Safety Check:** Skip deletion if URLs are missing or outdated
                var currentDate = DateTime.UtcNow.Date;

                if (urls == null || !urls.Any() || !urls.Any(url => url.LastModified?.Date == currentDate))
                {
                    Console.WriteLine("No URLs for the current day. Skipping file deletion.");
                }
                else
                {
                    // Convert URLs to file paths based on the Git directory
                    var sitemapFilePaths = urls
                        .Select(url => NormalizePath(downloader.GetFilePathFromUrl(url.Url, config.GitDirectory)))
                        .ToHashSet();

                    Console.WriteLine("Identifying existing HTML files...");
                    var existingHtmlFiles = Directory
                        .EnumerateFiles(config.GitDirectory, "*.html", SearchOption.AllDirectories)
                        .Select(NormalizePath)
                        .ToHashSet();

                    var excludedFiles = config.Exclusions
                        .Select(exclusion => NormalizePath(Path.Combine(config.GitDirectory, exclusion)))
                        .ToHashSet();

                    // Determine which files to delete
                    var filesToDelete = existingHtmlFiles
                        .Except(sitemapFilePaths)
                        .Except(excludedFiles)
                        .ToList();

                    Console.WriteLine("Downloading new or updated files...");
                    foreach (var url in urls)
                    {
                        await downloader.DownloadUrlAsync(url.Url, config.GitDirectory);
                    }

                    // Delete only if valid files are found for deletion
                    if (filesToDelete.Any())
                    {
                        Console.WriteLine("Deleting obsolete HTML files...");
                        foreach (var file in filesToDelete)
                        {
                            Console.WriteLine($"Deleting: {file}");
                            File.Delete(file);
                        }
                    }
                    else
                    {
                        Console.WriteLine("No files to delete.");
                    }
                }

                // Commit and push changes to the repository
                Console.WriteLine("Staging, committing, and pushing changes...");
                gitHelper.StageCommitAndPush("Synced files from sitemap, including deletions.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            finally
            {
                lockHelper.ReleaseLock();
            }

            Console.WriteLine("Done.");
            await Task.Delay(TimeSpan.FromSeconds(15)); // Async-friendly delay
        }
    }

    private static async Task<string> DownloadSitemapAsync(string sitemapUrl, string gitDirectory)
    {
        using var httpClient = new HttpClient();

        try
        {
            var response = await httpClient.GetAsync(sitemapUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var sitemapFileName = Path.GetFileName(new Uri(sitemapUrl).LocalPath);
            var sitemapFilePath = Path.Combine(gitDirectory, sitemapFileName);

            await File.WriteAllTextAsync(sitemapFilePath, content);
            return sitemapFilePath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading sitemap: {ex.Message}");
            throw;
        }
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(new Uri(path).LocalPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
}