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

            _appBase = new Uri(_appHostDomain.EndsWith("/") ? _appHostDomain : _appHostDomain + "/");
            _noAppBase = new Uri(_noAppHostDomain.EndsWith("/") ? _noAppHostDomain : _noAppHostDomain + "/");
        }

        public string RewriteContent(string content, string url)
        {
            var extension = Path.GetExtension(StripQuery(StripFragment(url)));
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

            RewriteAnchorsByClass(htmlDocument, "app-link", _appBase);
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
                if (IsNonHttpHref(href)) continue;

                var rewritten = RewriteUrlPreserveQueryAndFragment(href, baseUri);
                link.SetAttributeValue("href", rewritten);
            }
        }

        private string RewriteXmlUrls(string xmlContent)
        {
            // XML sitemaps don't carry query/fragment, simple host swap is fine.
            return xmlContent.Replace(_appHostDomain, _noAppHostDomain);
        }

        private static bool IsNonHttpHref(string href)
        {
            return href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("#"); // in-page anchor
        }

        /// <summary>
        /// Rewrites href so that the scheme + host come from baseUri while preserving
        /// the original path, query string, and fragment exactly as authored.
        /// Handles absolute, root-relative ("/foo"), tilde ("~/foo"), and
        /// document-relative ("foo/bar") hrefs.
        /// </summary>
        private static string RewriteUrlPreserveQueryAndFragment(string href, Uri baseUri)
        {
            // 1) Split off fragment FIRST (it appears after the query in the URL).
            string fragment = "";
            var hashIdx = href.IndexOf('#');
            if (hashIdx >= 0)
            {
                fragment = href.Substring(hashIdx); // includes leading '#'
                href = href.Substring(0, hashIdx);
            }

            // 2) Split off query.
            string query = "";
            var qIdx = href.IndexOf('?');
            if (qIdx >= 0)
            {
                query = href.Substring(qIdx); // includes leading '?'
                href = href.Substring(0, qIdx);
            }

            // 3) `href` is now just the path portion. Normalize "~/" to root-relative.
            if (href.StartsWith("~/"))
            {
                href = "/" + href.Substring(2);
            }

            // 4) Resolve the path under baseUri.
            string newPath;

            if (Uri.TryCreate(href, UriKind.Absolute, out var abs)
                && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            {
                // Absolute http(s) — keep its path, swap host.
                newPath = abs.AbsolutePath;
            }
            else if (string.IsNullOrEmpty(href))
            {
                // Pure "?query" or "#frag" href — keep baseUri's path.
                newPath = baseUri.AbsolutePath;
            }
            else if (href.StartsWith("/"))
            {
                // Root-relative — use as-is.
                newPath = href;
            }
            else
            {
                // Document-relative — resolve against baseUri.
                try
                {
                    var combined = new Uri(baseUri, href);
                    newPath = combined.AbsolutePath;
                }
                catch
                {
                    // Unparseable — return original untouched.
                    return href + query + fragment;
                }
            }

            // 5) Reassemble manually to preserve query/fragment EXACTLY as authored.
            //    (UriBuilder.Query/Fragment can re-encode, which we don't want here.)
            return $"{baseUri.Scheme}://{baseUri.Authority}{newPath}{query}{fragment}";
        }

        private static string StripFragment(string url)
        {
            var idx = url.IndexOf('#');
            return idx >= 0 ? url.Substring(0, idx) : url;
        }

        private static string StripQuery(string url)
        {
            var idx = url.IndexOf('?');
            return idx >= 0 ? url.Substring(0, idx) : url;
        }
    }
}