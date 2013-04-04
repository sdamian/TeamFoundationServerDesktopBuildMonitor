namespace BuildMonitor.Domain
{
    public class BuildInformationListViewModel
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string BuildTime { get; set; }
        public string SourceVersion { get; set; }

        public string[] ToStringArray()
        {
            return new[] { Name, Status,BuildTime,SourceVersion };
        }
    }
}