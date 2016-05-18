using System;

namespace BuildMonitor.Domain
{
    [Serializable]
	public class ServerBuild
	{
		public ServerBuild(string serverUrl, string buildUrl)
		{
			ServerUrl = serverUrl;
			BuildUrl = buildUrl;
		}

		public ServerBuild()
		{
		}

		public string ServerUrl { get; set; }
		public string BuildUrl { get; set; }
	}
}