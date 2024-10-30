namespace GitSiteSyncer.Utilities
{
    public class FileDownloader
    {
        private readonly HttpClient _client = new();
        private readonly ContentRewriter _rewriter;

        public FileDownloader(ContentRewriter rewriter)
        {
            _rewriter = rewriter;
        }

        public static async Task<string> DownloadSitemapAsync(string sitemapUrl, string gitDirectory)
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


        public async Task DownloadUrlAsync(string url, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Console.WriteLine($"Getting {url}");

                var response = await _client.GetStringAsync(url);
                var rewrittenContent = _rewriter.RewriteContent(response, url);
                var filePath = GetFilePathFromUrl(url, baseDirectory);
                var directoryPath = Path.GetDirectoryName(filePath);

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                await File.WriteAllTextAsync(filePath, rewrittenContent);
                Console.WriteLine($"Saved: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading {url}: {ex.Message}");
            }
        }

        public string GetFilePathFromUrl(string url, string baseDirectory)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.TrimEnd('/');

                // Handle root URLs by setting "index.html" as the default filename
                if (string.IsNullOrEmpty(path) || path == "/")
                {
                    path = "index.html";
                }
                else
                {
                    var lastSegment = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(Path.GetExtension(lastSegment)))
                    {
                        // If no extension is found, append ".html" to ensure it can be opened as a webpage
                        path += ".html";
                    }
                }

                // Replace URL path separators with the system's directory separators
                var filePath = Path.Combine(baseDirectory, path.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                return filePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing URL {url}: {ex.Message}");
                throw;
            }
        }
    }
}
