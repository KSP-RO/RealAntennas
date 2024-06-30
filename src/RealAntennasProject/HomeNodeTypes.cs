using RealAntennas.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RealAntennas
{
    public static class HomeNodeTypes
    {
        public static readonly Dictionary<string, List<string>> NameDict = new Dictionary<string, List<string>>();
        public static readonly Dictionary<string, List<RACommNetHome>> HomeDict = new Dictionary<string, List<RACommNetHome>>();

        public static bool loadOnceStateInitialized = false;
        public static bool initialized = false;

        internal static void Init(ConfigNode config)
        {
            if (loadOnceStateInitialized) return;

            NameDict.Clear();
            HomeDict.Clear();
            foreach (ConfigNode node in config.GetNodes("HomeNodeTypes"))
            {
                foreach (ConfigNode.Value v in node.values)
                {
                    NameDict[v.name] = v.value.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(s => s.Trim())
                                              .ToList();
                }
            }
            loadOnceStateInitialized = true;
        }

        internal static void RebuildHomesDict(Dictionary<string, RACommNetHome> groundStations)
        {
            if (!loadOnceStateInitialized)
            {
                // Homes can get created before RACommNetScenario static state is initialized
                var node = GameDatabase.Instance.GetConfigNode("RealAntennas/RealAntennasCommNetParams/RealAntennasCommNetParams");
                if (node == null) return;
                Init(node);
            }

            initialized = false;
            HomeDict.Clear();
            foreach (var kvp in NameDict)
            {
                var list = new List<RACommNetHome>();
                HomeDict.Add(kvp.Key, list);
                foreach (string homeName in kvp.Value)
                {
                    if (groundStations.TryGetValue(homeName, out RACommNetHome home))
                        list.Add(home);
                }
            }

            var lsList = groundStations.Values.Where(h => h.name == "LaunchSiteTrackingStation").ToList();
            HomeDict["LaunchSite"] = lsList;
            NameDict["LaunchSite"] = lsList.Select(h => h.nodeName).ToList();
            initialized = true;
        }

        internal static void Destroy()
        {
            // Release references to RACommNetHome instances and make sure that the start of next scene doesn't have stale data
            HomeDict.Clear();
            initialized = false;
        }
    }
}
