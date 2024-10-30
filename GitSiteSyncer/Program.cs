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

        // Ensure LockFileDirectory exists
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
                var urls = await sitemapReader.GetUrlsAsync(config.SitemapUrl);

                // Save the sitemap
                Console.WriteLine($"Downloading sitemap from {config.SitemapUrl}...");
                string sitemapFilePath = await FileDownloader.DownloadSitemapAsync(config.SitemapUrl, config.GitDirectory);
                Console.WriteLine($"Sitemap saved to: {sitemapFilePath}");

                // Map URLs to local file paths
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

                // Determine which files to delete: Local files not in the sitemap and not excluded
                var filesToDelete = existingHtmlFiles
                    .Except(sitemapFilePaths)
                    .Except(excludedFiles)
                    .ToList();

                // **Download or update files only if modified within the `DaysToConsider` range**
                var cutoffDate = DateTime.UtcNow.AddDays(-config.DaysToConsider);

                Console.WriteLine("Downloading new or updated files...");
                foreach (var url in urls)
                {
                    if (url.LastModified == null || url.LastModified >= cutoffDate)
                    {
                        Console.WriteLine($"Updating: {url.Url}");
                        await downloader.DownloadUrlAsync(url.Url, config.GitDirectory);
                    }
                    else
                    {
                        Console.WriteLine($"Skipping outdated URL: {url.Url}");
                    }
                }

                // Delete obsolete files (only those not in the sitemap and not excluded)
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

    private static string NormalizePath(string path) =>
        Path.GetFullPath(new Uri(path).LocalPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
}
