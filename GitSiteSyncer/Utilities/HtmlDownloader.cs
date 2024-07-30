namespace GitSiteSyncer.Utilities
{
    public class HtmlDownloader
    {
        private HttpClient _client = new();
        private HtmlRewriter _rewriter;

        public HtmlDownloader(HtmlRewriter rewriter)
        {
            _rewriter = rewriter;
        }

        public async Task DownloadUrlAsync(string url, string baseDirectory)
        {
            Console.WriteLine($"Getting {url}");

            var response = await _client.GetStringAsync(url);
            var rewrittenContent = _rewriter.RewriteUrls(response);
            var filePath = GetFilePathFromUrl(url, baseDirectory);
            var directoryPath = Path.GetDirectoryName(filePath);

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            await File.WriteAllTextAsync(filePath, rewrittenContent);
        }

        private string GetFilePathFromUrl(string url, string baseDirectory)
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath.TrimEnd('/');

            if (string.IsNullOrEmpty(path) || path == "/")
            {
                path = "index.html"; // Default file name for root URL
            }
            else
            {
                path = path.Trim('/') + ".html";
            }

            var filePath = Path.Combine(baseDirectory, path.Replace("/", Path.DirectorySeparatorChar.ToString()));
            return filePath;
        }
    }
}