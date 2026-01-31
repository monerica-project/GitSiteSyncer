using HtmlAgilityPack;

namespace GitSiteSyncer.Utilities
{
    public class ContentRewriter
    {
        private readonly string _appHostDomain;
        private readonly string _noAppHostDomain;

        // base URIs (normalized once)
        private readonly Uri _appBase;
        private readonly Uri _noAppBase;

        public ContentRewriter(string appHostDomain, string noAppHostDomain)
        {
            if (string.IsNullOrWhiteSpace(appHostDomain))
                throw new ArgumentException("appHostDomain is required", nameof(appHostDomain));
            if (string.IsNullOrWhiteSpace(noAppHostDomain))
                throw new ArgumentException("noAppHostDomain is required", nameof(noAppHostDomain));

            _appHostDomain = appHostDomain.TrimEnd('/');
            _noAppHostDomain = noAppHostDomain.TrimEnd('/');

            // Ensure these are absolute base URIs
            _appBase = new Uri(_appHostDomain.EndsWith("/") ? _appHostDomain : _appHostDomain + "/");
            _noAppBase = new Uri(_noAppHostDomain.EndsWith("/") ? _noAppHostDomain : _noAppHostDomain + "/");
        }

        public string RewriteContent(string content, string url)
        {
            var extension = Path.GetExtension(FileDownloader.RemoveFragmentAndQuery(url));
            if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
            {
                return RewriteXmlUrls(content);
            }

            return RewriteHtmlUrls(content);
        }

        private string RewriteHtmlUrls(string htmlContent)
        {
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(htmlContent);

            // Rewrite links with class "app-link"
            RewriteAnchorsByClass(htmlDocument, "app-link", _appBase);

            // Rewrite links with class "no-app-link"
            RewriteAnchorsByClass(htmlDocument, "no-app-link", _noAppBase);

            using var writer = new StringWriter();
            htmlDocument.Save(writer);
            return writer.ToString();
        }

        private void RewriteAnchorsByClass(HtmlDocument doc, string className, Uri baseUri)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//a[contains(@class, '{className}') and @href]");
            if (nodes == null) return;

            foreach (var link in nodes)
            {
                var href = link.GetAttributeValue("href", string.Empty)?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(href)) continue;

                // Skip non-http navigations
                if (IsNonHttpHref(href)) continue;

                var rewritten = RewriteUrlPreserveQueryAndFragment(href, baseUri);
                link.SetAttributeValue("href", rewritten);
            }
        }

        private string RewriteXmlUrls(string xmlContent)
        {
            // This is fine; XML sitemaps won’t use fragments anyway.
            return xmlContent.Replace(_appHostDomain, _noAppHostDomain);
        }

        private static bool IsNonHttpHref(string href)
        {
            // do not touch these schemes
            return href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("#"); // in-page anchor
        }

        /// <summary>
        /// Rewrites href to be absolute under baseUri, preserving ?query and #fragment.
        /// Handles absolute, /root-relative, ~/tilde, and relative paths.
        /// </summary>
        private string RewriteUrlPreserveQueryAndFragment(string href, Uri baseUri)
        {
            // Preserve fragment/query explicitly:
            // - For absolute hrefs, Uri.Fragment has the '#...'
            // - For relative hrefs, we can parse it too, but easiest is:
            //   split into (coreWithoutQueryFragment) + (query+fragment) then rebuild.
            var (query, fragment) = FileDownloader.GetQueryAndFragment(href);
            var core = StripQueryAndFragment(href);

            // Normalize "~/" to root-relative
            if (core.StartsWith("~/"))
            {
                core = "/" + core.Substring(2);
            }

            // Absolute URL
            if (Uri.TryCreate(core, UriKind.Absolute, out var abs))
            {
                // Replace host by projecting path+query onto baseUri,
                // but query already extracted; use abs.AbsolutePath
                var builder = new UriBuilder(baseUri)
                {
                    Path = abs.AbsolutePath.TrimStart('/'),
                    Query = query.StartsWith("?") ? query.Substring(1) : query,
                    Fragment = fragment.StartsWith("#") ? fragment.Substring(1) : fragment
                };
                return builder.Uri.ToString();
            }

            // Relative URL (root-relative or relative)
            if (Uri.TryCreate(core, UriKind.Relative, out var rel))
            {
                // Make absolute using baseUri
                var combined = new Uri(baseUri, rel);

                var builder = new UriBuilder(combined)
                {
                    Query = query.StartsWith("?") ? query.Substring(1) : query,
                    Fragment = fragment.StartsWith("#") ? fragment.Substring(1) : fragment
                };
                return builder.Uri.ToString();
            }

            // If we can’t parse it, return original untouched
            return href;
        }

        private static string StripQueryAndFragment(string url)
        {
            // Remove # then ?
            var noFrag = FileDownloader.RemoveFragment(url);
            var qIndex = noFrag.IndexOf('?');
            return qIndex >= 0 ? noFrag.Substring(0, qIndex) : noFrag;
        }
    }
}