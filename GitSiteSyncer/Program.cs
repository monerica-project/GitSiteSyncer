using System.Net.Http;
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

            // Realistic timeout + a UA so picky upstreams (Cloudflare, hosts that 403 the
            // empty default UA, etc.) actually serve us.
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "GitSiteSyncer/1.0 (+https://monerica.com)");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            // Windows = case-insensitive paths, Linux/macOS = case-sensitive.
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

            // Track failures so we can summarize at the end AND skip destructive deletes
            // when something went wrong this run.
            var failedDownloads = new List<(string Url, string Reason)>();

            try
            {
                GitHelper gitHelper = new GitHelper(config.GitDirectory, config.GitCredentials);

                Console.WriteLine("Synchronizing with remote repository...");
                gitHelper.ForceSyncRepo();

                SitemapReader sitemapReader = new SitemapReader(httpClient);
                ContentRewriter rewriter = new ContentRewriter(config.AppHostDomain, config.NoAppHostDomain);
                FileDownloader downloader = new FileDownloader(rewriter, config, httpClient);

                Console.WriteLine("Fetching sitemap URLs...");
                var urls = await sitemapReader.GetUrlsAsync(config.SitemapUrl);

                Console.WriteLine($"Downloading sitemap from {config.SitemapUrl}...");
                string sitemapFilePath = await downloader.DownloadSitemapAsync(config.SitemapUrl, config.GitDirectory);
                Console.WriteLine($"Sitemap saved to: {sitemapFilePath}");

                var sitemapFilePaths = urls
                    .Select(url => NormalizePath(downloader.GetFilePathFromUrl(url.Url, config.GitDirectory)))
                    .ToHashSet(pathComparer);

                Console.WriteLine("Identifying existing HTML files...");
                var existingHtmlFiles = Directory
                    .EnumerateFiles(config.GitDirectory, "*.html", SearchOption.AllDirectories)
                    .Select(NormalizePath)
                    .ToHashSet(pathComparer);

                var excludedFiles = config.Exclusions
                    .Select(exclusion => NormalizePath(Path.Combine(config.GitDirectory, exclusion)))
                    .ToHashSet(pathComparer);

                var filesToDelete = existingHtmlFiles
                    .Except(sitemapFilePaths, pathComparer)
                    .Except(excludedFiles, pathComparer)
                    .ToList();

                Console.WriteLine($"Downloading all {urls.Count} sitemap URLs...");
                foreach (var url in urls)
                {
                    Console.WriteLine($"Updating: {url.Url}");
                    var ok = await downloader.DownloadUrlAsync(url.Url, config.GitDirectory);
                    if (!ok)
                    {
                        failedDownloads.Add((url.Url, "download failed (see log above)"));
                    }
                }

                // Safety net: if anything failed this run, do NOT run the delete pass.
                // A transient 5xx or rate-limit shouldn't be allowed to wipe pages.
                if (failedDownloads.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"WARNING: {failedDownloads.Count} download(s) failed this run:");
                    foreach (var (failedUrl, reason) in failedDownloads)
                    {
                        Console.WriteLine($"  - {failedUrl}  ({reason})");
                    }
                    Console.WriteLine("Skipping deletion pass to avoid removing pages that may only be temporarily unreachable.");
                }
                else if (filesToDelete.Any())
                {
                    Console.WriteLine("Deleting obsolete HTML files...");
                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            Console.WriteLine($"Deleting: {file}");
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not delete {file}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No files to delete.");
                }

                Console.WriteLine("Staging, committing, and pushing changes...");
                gitHelper.StageCommitAndPush("Synced files from sitemap, including deletions.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                lockHelper.ReleaseLock();
            }

            Console.WriteLine("Done.");
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    }

    // Don't uppercase — keep the real filesystem path so File.Delete works on Linux.
    // OS-aware HashSet comparers handle case-insensitive matching on Windows.
    private static string NormalizePath(string path) =>
        Path.GetFullPath(new Uri(path).LocalPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}