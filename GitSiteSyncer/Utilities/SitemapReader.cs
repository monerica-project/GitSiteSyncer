using GitSiteSyncer.Models;
using System.Xml.Linq;

namespace GitSiteSyncer.Utilities
{
    public class SitemapReader
    {
        public async Task<List<SitemapEntry>> GetUrlsAsync(string sitemapUrl)
        {
            List<SitemapEntry> entries = new List<SitemapEntry>();
            using (HttpClient client = new HttpClient())
            {
                var sitemap = await client.GetStringAsync(sitemapUrl);
                var xml = XDocument.Parse(sitemap);
                XNamespace ns = "https://www.sitemaps.org/schemas/sitemap/0.9"; // XML namespace

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

                        // Add every entry from the sitemap to the list, regardless of modification date.
                        entries.Add(new SitemapEntry
                        {
                            Url = url,
                            LastModified = lastModified
                        });
                    }
                }
            }
            return entries;
        }
    }
}