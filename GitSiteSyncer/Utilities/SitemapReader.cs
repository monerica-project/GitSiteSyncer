using System.Xml.Linq;
namespace GitSiteSyncer.Utilities
{
    public class SitemapReader
    {
        public async Task<List<string>> GetUrlsAsync(string sitemapUrl, int daysToConsider)
        {
            List<string> urls = new List<string>();
            using (HttpClient client = new HttpClient())
            {
                var sitemap = await client.GetStringAsync(sitemapUrl);
                var xml = XDocument.Parse(sitemap);
                XNamespace ns = "https://www.sitemaps.org/schemas/sitemap/0.9"; // The namespace from the XML file
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToConsider);

                foreach (var urlElement in xml.Descendants(ns + "url"))
                {
                    var locElement = urlElement.Element(ns + "loc");
                    var lastmodElement = urlElement.Element(ns + "lastmod");

                    if (locElement != null)
                    {
                        var url = locElement.Value;
                        if (lastmodElement != null)
                        {
                            if (DateTime.TryParse(lastmodElement.Value, out DateTime lastmodDate))
                            {
                                if (lastmodDate >= cutoffDate)
                                {
                                    urls.Add(url);
                                }
                            }
                        }
                        else
                        {
                            // If no lastmod, include the URL (optional based on requirements)
                            urls.Add(url);
                        }
                    }
                }
            }
            return urls;
        }
    }
}