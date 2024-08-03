using HtmlAgilityPack;

namespace GitSiteSyncer.Utilities
{
    public class ContentRewriter
    {
        private readonly string _appHostDomain;
        private readonly string _noAppHostDomain;

        public ContentRewriter(string appHostDomain, string noAppHostDomain)
        {
            _appHostDomain = appHostDomain;
            _noAppHostDomain = noAppHostDomain;
        }

        public string RewriteContent(string content, string url)
        {
            var extension = Path.GetExtension(url);
            if (extension == ".xml")
            {
                return RewriteXmlUrls(content);
            }
            else
            {
                return RewriteHtmlUrls(content);
            }
        }

        private string RewriteHtmlUrls(string htmlContent)
        {
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(htmlContent);

            // Rewrite links with class "app-link"
            var appLinks = htmlDocument.DocumentNode.SelectNodes("//a[contains(@class, 'app-link') and @href]");
            if (appLinks != null)
            {
                foreach (var link in appLinks)
                {
                    var href = link.GetAttributeValue("href", string.Empty);
                    link.SetAttributeValue("href", RewriteUrl(href, _appHostDomain));
                }
            }

            // Rewrite links with class "no-app-link"
            var noAppLinks = htmlDocument.DocumentNode.SelectNodes("//a[contains(@class, 'no-app-link') and @href]");
            if (noAppLinks != null)
            {
                foreach (var link in noAppLinks)
                {
                    var href = link.GetAttributeValue("href", string.Empty);
                    link.SetAttributeValue("href", RewriteUrl(href, _noAppHostDomain));
                }
            }

            using (var writer = new StringWriter())
            {
                htmlDocument.Save(writer);
                return writer.ToString();
            }
        }

        private string RewriteXmlUrls(string xmlContent)
        {
            return xmlContent
                .Replace(_appHostDomain, _noAppHostDomain);
        }

        private string RewriteUrl(string url, string newHostDomain)
        {
            // Ensure the URL is absolute and replace the host with the new host domain
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri originalUri))
            {
                var newUri = new Uri(newHostDomain + originalUri.PathAndQuery);
                return newUri.ToString();
            }
            else if (Uri.TryCreate(url, UriKind.Relative, out originalUri))
            {
                var newUri = new Uri(new Uri(newHostDomain), originalUri);
                return newUri.ToString();
            }

            return url; // Return the original URL if it's not valid
        }
    }
}
