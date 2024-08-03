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

        public async Task DownloadUrlAsync(string url, string baseDirectory)
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
                var lastSegment = Path.GetFileName(path);
                if (string.IsNullOrEmpty(Path.GetExtension(lastSegment)))
                {
                    // No extension found, default to .html
                    path += ".html";
                }
            }

            var filePath = Path.Combine(baseDirectory, path.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
            return filePath;
        }
    }
}