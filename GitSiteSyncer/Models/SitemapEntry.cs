namespace GitSiteSyncer.Models
{
    public class SitemapEntry
    {
        required public string Url { get; set; }
        public DateTime? LastModified { get; set; }
    }
}