using GitSiteSyncer.Models;
using System.Xml.Linq;

namespace GitSiteSyncer.Utilities
{
    public class SitemapReader
    {
        public async Task<List<SitemapEntry>> GetUrlsAsync(string sitemapUrl, int daysToConsider)
        {
            List<SitemapEntry> entries = new List<SitemapEntry>();
            using (HttpClient client = new HttpClient())
            {
                var sitemap = await client.GetStringAsync(sitemapUrl);
                var xml = XDocument.Parse(sitemap);
                XNamespace ns = "https://www.sitemaps.org/schemas/sitemap/0.9"; // XML namespace

                var cutoffDate = DateTime.UtcNow.AddDays(-daysToConsider);

                foreach (var urlElement in xml.Descendants(ns + "url"))
                {
                    var locElement = urlElement.Element(ns + "loc");
                    var lastmodElement = urlElement.Element(ns + "lastmod");

                    if (locElement != null)
                    {
                        string url = locElement.Value;
                        DateTime? lastModified = null;

                        if (lastmodElement != null && DateTime.TryParse(lastmodElement.Value, out DateTime lastmodDate))
                        {
                            lastModified = lastmodDate;
                        }

                        // Add to entries only if last modified is within the cutoff date
                        if (lastModified == null || lastModified >= cutoffDate)
                        {
                            entries.Add(new SitemapEntry
                            {
                                Url = url,
                                LastModified = lastModified
                            });
                        }
                    }
                }
            }
            return entries;
        }
    }
}