namespace GitSiteSyncer.Models
{
    public class AppConfig
    {
        public string GitDirectory { get; set; }
        public string LockFileDirectory { get; set; }
        public GitCredentials GitCredentials { get; set; }
        public string SitemapUrl { get; set; }
        public int DaysToConsider { get; set; }
        public string AppHostDomain { get; set; } // New host domain for "app-link"
        public string NoAppHostDomain { get; set; } // New host domain for "no-app-link"

        /// <summary>
        /// List of HTML files to exclude from deletion (e.g., "404.html", "offline.html").
        /// </summary>
        public List<string> Exclusions { get; set; } = new List<string>();
    }
}