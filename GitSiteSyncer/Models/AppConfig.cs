namespace GitSiteSyncer.Models
{
    public class AppConfig
    {
        public string GitDirectory { get; set; }
        public string LockFileDirectory { get; set; }
        public GitCredentials GitCredentials { get; set; }
        public string SitemapUrl { get; set; }
        public int DaysToConsider { get; set; }
        public string AppHostDomain { get; set; }
    }
}