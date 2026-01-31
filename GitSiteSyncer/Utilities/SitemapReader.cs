using GitSiteSyncer.Models;
using System.Xml.Linq;

namespace GitSiteSyncer.Utilities
{
    public class SitemapReader
    {
        private readonly HttpClient _client;

        public SitemapReader(HttpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<List<SitemapEntry>> GetUrlsAsync(string sitemapUrl)
        {
            if (string.IsNullOrWhiteSpace(sitemapUrl))
                throw new ArgumentException("sitemapUrl is empty.", nameof(sitemapUrl));

            List<SitemapEntry> entries = new();

            // NOTE: fragments (#...) are irrelevant to HTTP, but we keep sitemapUrl as provided.
            // If sitemapUrl contains a fragment, remove it for request:
            var requestUrl = FileDownloader.RemoveFragment(sitemapUrl);

            var sitemap = await _client.GetStringAsync(requestUrl);
            var xml = XDocument.Parse(sitemap);

            // Your sitemap namespace is correct
            XNamespace ns = "https://www.sitemaps.org/schemas/sitemap/0.9";

            foreach (var urlElement in xml.Descendants(ns + "url"))
            {
                var locElement = urlElement.Element(ns + "loc");
                var lastmodElement = urlElement.Element(ns + "lastmod");

                if (locElement == null) continue;

                string url = locElement.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(url)) continue;

                DateTime? lastModified = null;
                if (lastmodElement != null && DateTime.TryParse(lastmodElement.Value, out DateTime lastmodDate))
                {
                    lastModified = lastmodDate;
                }

                entries.Add(new SitemapEntry
                {
                    Url = url,
                    LastModified = lastModified
                });
            }

            return entries;
        }
    }
}
