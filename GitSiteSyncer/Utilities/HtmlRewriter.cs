using HtmlAgilityPack;

namespace GitSiteSyncer.Utilities
{
    public class HtmlRewriter
    {
        private readonly string _newHostDomain;

        public HtmlRewriter(string newHostDomain)
        {
            _newHostDomain = newHostDomain;
        }

        public string RewriteUrls(string htmlContent)
        {
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(htmlContent);

            // Rewrite form actions
            var forms = htmlDocument.DocumentNode.SelectNodes("//form[@action]");
            if (forms != null)
            {
                foreach (var form in forms)
                {
                    var action = form.GetAttributeValue("action", string.Empty);
                    form.SetAttributeValue("action", RewriteUrl(action));
                }
            }

            // Rewrite links with class "app-link"
            var links = htmlDocument.DocumentNode.SelectNodes("//a[contains(@class, 'app-link') and @href]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", string.Empty);
                    link.SetAttributeValue("href", RewriteUrl(href));
                }
            }

            using (var writer = new StringWriter())
            {
                htmlDocument.Save(writer);
                return writer.ToString();
            }
        }

        private string RewriteUrl(string url)
        {
            // Ensure the URL is absolute and replace the host with the new host domain
            Uri originalUri;
            if (Uri.TryCreate(url, UriKind.Absolute, out originalUri))
            {
                var newUri = new Uri(_newHostDomain + originalUri.PathAndQuery);
                return newUri.ToString();
            }
            else if (Uri.TryCreate(url, UriKind.Relative, out originalUri))
            {
                var newUri = new Uri(new Uri(_newHostDomain), originalUri);
                return newUri.ToString();
            }

            return url; // Return the original URL if it's not valid
        }
    }
}