using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BuildMonitor.Domain
{
    [Serializable]
    public class ServerBuildCollection : Collection<ServerBuild>
    {
        public ServerBuildCollection()
        {
            
        }

        public ServerBuildCollection(IEnumerable<ServerBuild> serverBuilds)
        {
            foreach (ServerBuild build in serverBuilds)
            {
                Add(build);
            }
        }
    }
}