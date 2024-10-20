using GitSiteSyncer.Models;
using GitSiteSyncer.Utilities;
using System.Linq;

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

                // Step 1: Ensure the latest remote changes are synced
                Console.WriteLine("Synchronizing with remote repository...");
                gitHelper.ForceSyncRepo();

                SitemapReader sitemapReader = new SitemapReader();
                ContentRewriter rewriter = new ContentRewriter(config.AppHostDomain, config.NoAppHostDomain);
                FileDownloader downloader = new FileDownloader(rewriter);

                Console.WriteLine("Fetching sitemap URLs...");
                var urls = await sitemapReader.GetUrlsAsync(config.SitemapUrl, config.DaysToConsider);

                // Convert URLs to file paths based on the Git directory
                var sitemapFilePaths = urls
                    .Select(url => downloader.GetFilePathFromUrl(url, config.GitDirectory))
                    .ToHashSet();

                Console.WriteLine("Identifying existing HTML files...");
                var existingHtmlFiles = Directory
                    .EnumerateFiles(config.GitDirectory, "*.html", SearchOption.AllDirectories)
                    .ToHashSet();

                // Exclude specific HTML files listed in the config
                var excludedFiles = config.Exclusions
                    .Select(exclusion => Path.Combine(config.GitDirectory, exclusion))
                    .ToHashSet();

                var filesToDelete = existingHtmlFiles
                    .Except(sitemapFilePaths) // Files not in the sitemap
                    .Except(excludedFiles) // Exclude files from deletion
                    .ToList();

                Console.WriteLine("Downloading new or updated files...");
                foreach (var url in urls)
                {
                    await downloader.DownloadUrlAsync(url, config.GitDirectory);
                }

                Console.WriteLine("Deleting obsolete HTML files...");
                foreach (var file in filesToDelete)
                {
                    Console.WriteLine($"Deleting: {file}");
                    File.Delete(file);
                }

                // Commit and push all changes to the Git repository
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
            await Task.Delay(TimeSpan.FromSeconds(15)); // Use async-friendly delay
        }
    }
}
