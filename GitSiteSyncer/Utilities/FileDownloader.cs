using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GitSiteSyncer.Models;

namespace GitSiteSyncer.Utilities
{
    /// <summary>
    /// Downloads sitemap + pages, rewrites content, and saves to disk.
    /// Preserves fragments (#...) for rewriting purposes, but strips them for HTTP requests and file paths.
    /// Reuses an injected HttpClient (do NOT new HttpClient per call).
    /// </summary>
    public class FileDownloader
    {
        private readonly HttpClient _client;
        private readonly ContentRewriter _rewriter;
        private readonly AppConfig _appConfig;

        public FileDownloader(ContentRewriter rewriter, AppConfig appConfig, HttpClient httpClient)
        {
            _rewriter = rewriter ?? throw new ArgumentNullException(nameof(rewriter));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> DownloadSitemapAsync(string sitemapUrl, string gitDirectory, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sitemapUrl))
                throw new ArgumentException("sitemapUrl is empty.", nameof(sitemapUrl));
            if (string.IsNullOrWhiteSpace(gitDirectory))
                throw new ArgumentException("gitDirectory is empty.", nameof(gitDirectory));

            // Fragment never goes to server, strip it for the request.
            var requestUrl = RemoveFragment(sitemapUrl);

            try
            {
                using var response = await _client.GetAsync(requestUrl, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                // Replace all occurrences of AppHostDomain with NoAppHostDomain in sitemap content
                if (!string.IsNullOrEmpty(_appConfig.AppHostDomain) && !string.IsNullOrEmpty(_appConfig.NoAppHostDomain))
                {
                    content = content.Replace(_appConfig.AppHostDomain, _appConfig.NoAppHostDomain);
                }

                var sitemapFileName = Path.GetFileName(new Uri(requestUrl).LocalPath);
                var sitemapFilePath = Path.Combine(gitDirectory, sitemapFileName);

                Directory.CreateDirectory(gitDirectory);
                await File.WriteAllTextAsync(sitemapFilePath, content, ct).ConfigureAwait(false);

                return sitemapFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading sitemap: {ex.Message}");
                throw;
            }
        }

        public async Task DownloadUrlAsync(string url, string baseDirectory, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            // Keep original (may contain ? and/or #) so the rewriter can preserve them.
            var originalUrl = url.Trim();

            // HTTP never sends #fragment; strip it for request.
            var requestUrl = RemoveFragment(originalUrl);

            // Build file path from a URL without ? or #
            var filePath = GetFilePathFromUrl(requestUrl, baseDirectory);

            try
            {
                Console.WriteLine($"Getting {requestUrl}");

                using var response = await _client.GetAsync(requestUrl, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                // IMPORTANT: pass originalUrl (with #) so rewritten hrefs can retain fragments.
                var rewrittenContent = _rewriter.RewriteContent(html, originalUrl);

                var directoryPath = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                await File.WriteAllTextAsync(filePath, rewrittenContent, ct).ConfigureAwait(false);
                Console.WriteLine($"Saved: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading {requestUrl}: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts a URL into a local .html path under baseDirectory.
        /// NOTE: Query (?) and Fragment (#) are always removed for filesystem paths.
        /// </summary>
        public string GetFilePathFromUrl(string url, string baseDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    throw new ArgumentException("url is empty.", nameof(url));
                if (string.IsNullOrWhiteSpace(baseDirectory))
                    throw new ArgumentException("baseDirectory is empty.", nameof(baseDirectory));

                // Do not allow query/fragment into file paths
                var safeUrl = RemoveFragmentAndQuery(url);

                var uri = new Uri(safeUrl);
                var path = uri.AbsolutePath.TrimEnd('/');

                // Root -> index.html
                if (string.IsNullOrEmpty(path) || path == "/")
                {
                    path = "index.html";
                }
                else
                {
                    var lastSegment = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(Path.GetExtension(lastSegment)))
                    {
                        // No extension => save as .html
                        path += ".html";
                    }
                }

                var relativePath = path.TrimStart('/')
                    .Replace("/", Path.DirectorySeparatorChar.ToString());

                return Path.Combine(baseDirectory, relativePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing URL {url}: {ex.Message}");
                throw;
            }
        }

        // -----------------------------
        // Fragment/query helpers
        // -----------------------------

        /// <summary>Removes "#fragment" from a URL string.</summary>
        public static string RemoveFragment(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;

            var hashIndex = url.IndexOf('#');
            return hashIndex >= 0 ? url.Substring(0, hashIndex) : url;
        }

        /// <summary>Removes both "?query" and "#fragment" from a URL string.</summary>
        public static string RemoveFragmentAndQuery(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;

            // Remove fragment first
            var noFrag = RemoveFragment(url);

            // Then remove query
            var qIndex = noFrag.IndexOf('?');
            return qIndex >= 0 ? noFrag.Substring(0, qIndex) : noFrag;
        }

        /// <summary>
        /// Extracts "?query" and "#fragment" (including their leading characters) from a URL string.
        /// Works for absolute URLs reliably and supports simple relative fallback.
        /// </summary>
        public static (string Query, string Fragment) GetQueryAndFragment(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return (string.Empty, string.Empty);

            if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
            {
                return (abs.Query ?? string.Empty, abs.Fragment ?? string.Empty);
            }

            // Relative fallback
            string fragment = string.Empty;
            string query = string.Empty;

            var hashIndex = url.IndexOf('#');
            if (hashIndex >= 0)
            {
                fragment = url.Substring(hashIndex); // includes '#'
                url = url.Substring(0, hashIndex);
            }

            var qIndex = url.IndexOf('?');
            if (qIndex >= 0)
            {
                query = url.Substring(qIndex); // includes '?'
            }

            return (query, fragment);
        }

        /// <summary>
        /// Appends original "?query" and "#fragment" onto a rewritten URL.
        /// Useful if a rewriting step outputs a path but needs to preserve anchors.
        /// </summary>
        public static string AppendQueryAndFragment(string rewrittenUrl, string originalUrl)
        {
            if (string.IsNullOrWhiteSpace(rewrittenUrl)) return rewrittenUrl;
            var (q, f) = GetQueryAndFragment(originalUrl);
            return rewrittenUrl + q + f;
        }
    }
}
