using GitSiteSyncer.Models;
using GitSiteSyncer.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Running...");
        string appRootDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string configFilePath = Path.Combine(appRootDirectory, "appsettings.json");

        AppConfig config = ConfigLoader.LoadConfig(configFilePath);

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
                SitemapReader sitemapReader = new SitemapReader();
                HtmlRewriter rewriter = new HtmlRewriter(config.AppHostDomain, config.NoAppHostDomain);
                HtmlDownloader downloader = new HtmlDownloader(rewriter);
                GitHelper gitHelper = new GitHelper(config.GitDirectory, config.GitCredentials);

                var urls = await sitemapReader.GetUrlsAsync(config.SitemapUrl, config.DaysToConsider);

                foreach (var url in urls)
                {
                    await downloader.DownloadUrlAsync(url, config.GitDirectory);
                }

                gitHelper.CommitAndPush("Updated files from sitemap");
            }
            finally
            {
                lockHelper.ReleaseLock();
            }

            Console.WriteLine("done.");
        }
    }
}