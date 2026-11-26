using CommNet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using System.Text;

namespace RealAntennas
{
    public class RACommNetwork : CommNetwork
    {
        protected static readonly string ModTag = "[RealAntennasCommNetwork]";

        private float lastRun = 0f;
        private readonly System.Diagnostics.Stopwatch RebuildStopWatch = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch PathfindWatch = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch PrecomputeLateWatch = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch tempWatch = new System.Diagnostics.Stopwatch();
        internal readonly Precompute.Precompute precompute = new Precompute.Precompute();

        public List<CommNode> Nodes { get => nodes; }
        public RealAntenna DebugAntenna => connectionDebugger?.antenna;
        public Network.ConnectionDebugger connectionDebugger = null;
        public readonly EventVoid NetworkUpdateComplete = new EventVoid("Network Rebuild Complete");
        public double LastUpdateUT { get; private set; } = 0;

        // True when a node was added or removed since the precompute data was last initialized.
        // The precompute data refers to nodes by their index in the node list, so it is stale once the list changes.
        public bool NodeListChanged { get; private set; }
        public void ClearNodeListChanged() => NodeListChanged = false;

        public override CommNode Add(CommNode conn)
        {
            if (!(conn is RACommNode))
            {
                Debug.LogWarning($"{ModTag} Wrong commnode type, so ignoring.");
                return conn;
            }
            NodeListChanged = true;
            return base.Add(conn);
        }

        public override bool Remove(CommNode conn)
        {
            NodeListChanged = true;
            return base.Remove(conn);
        }

        protected override bool SetNodeConnection(CommNode a, CommNode b)
        {
            Debug.LogError($"[RACommNetwork] SetNodeConnection called, but it should never be!");
            return false;
        }

        protected override void PostUpdateNodes()
        {
            if (TimeToValidate()) { Validate(); LogState(); }
            base.PostUpdateNodes();
        }

        protected override bool TryConnect(CommNode a, CommNode b, double distance, bool aCanRelay = true, bool bCanRelay = true, bool bothRelay = true)
        {
            Debug.LogError($"[RACommNetwork] TryConnect called, but it should never be!");
            return false;
        }

        public void MakeLink(RealAntenna fwdTx,
                               RealAntenna fwdRx,
                               RealAntenna revTx,
                               RealAntenna revRx,
                               RACommNode a,
                               RACommNode b,
                               double distance,
                               double FwdDataRate,
                               double RevDataRate,
                               double FwdBestDataRate,
                               double FwdMetric,
                               double RevMetric
                               )
        {
            RACommLink link = Connect(a, b, distance) as RACommLink;
            link.aCanRelay = true;
            link.bCanRelay = true;      // All antennas can relay.
            link.bothRelay = link.aCanRelay && link.bCanRelay;

            link.FwdAntennaTx = fwdTx;
            link.FwdAntennaRx = fwdRx;
            link.RevAntennaTx = revTx;
            link.RevAntennaRx = revRx;
            link.FwdDataRate = FwdDataRate;
            link.RevDataRate = RevDataRate;
            link.cost = link.CostFunc((FwdDataRate + RevDataRate) / 2);
            link.FwdMetric = FwdMetric;
            link.RevMetric = RevMetric;
            if (FwdBestDataRate < FwdDataRate)
                Debug.LogWarning($"{ModTag} Detected actual rate {FwdDataRate} greater than expected max {FwdBestDataRate} for antennas {link.FwdAntennaTx} and {link.FwdAntennaRx}");

            link.Update(Math.Min(link.FwdMetric, link.RevMetric));
        }

        protected override CommLink Connect(CommNode a, CommNode b, double distance)
        {
            a.TryGetValue(b, out CommLink foundLink);
            if (foundLink == null)
            {
                foundLink = new RACommLink();
                foundLink.Set(a, b, 0, 0);
                Links.Add(foundLink);
                a.Add(b, foundLink);
                b.Add(a, foundLink);
            }
            return foundLink;
        }

        private bool TimeToValidate()
        {
            bool res = RACommNetScenario.debugWalkLogging && (Time.timeSinceLevelLoad > lastRun + RACommNetScenario.debugWalkInterval);
            if (res) lastRun = Time.timeSinceLevelLoad;
            return res;
        }

        public int HopsToHome(RACommNode start)
        {
            CommPath path = new CommPath();
            if ((start == null) || !FindHome(start, path)) return -1;
            return path.Count;
        }

        public double MaxDataRateToHome(RACommNode start)
        {
            CommPath path = new CommPath();
            if ((start == null) || !FindHome(start, path)) return 0;
            double data_rate = double.MaxValue;
            foreach (CommLink l in path)
            {
                RACommLink link = l.start[l.end] as RACommLink;
                double linkRate = link.start.Equals(l.start) ? link.FwdDataRate : link.RevDataRate;
                data_rate = Math.Min(data_rate, linkRate);
            }
            return data_rate;
        }
        private bool calculating = false;

        private bool IsPaused => (KSCPauseMenu.Instance && KSCPauseMenu.Instance.enabled) || (PauseMenu.exists && PauseMenu.isOpen);
        public virtual void StartRebuild(bool compute)
        {
            isDirty = false;
            calculating = compute;
            if (OnNetworkPreUpdate is Action)
                OnNetworkPreUpdate();
            PreUpdateNodes();
            UpdateOccluders();
            if (compute)
            {
                Profiler.BeginSample("RealAntennas StartRebuild");
                tempWatch.Reset();
                tempWatch.Start();
                precompute.DoThings();
                tempWatch.Stop();
                Profiler.EndSample();
                (RACommNetScenario.Instance as RACommNetScenario).metrics.AddMeasurement("EarlyRebuild", tempWatch.Elapsed.TotalMilliseconds);
            }
        }
        public virtual void CompleteRebuild()
        {
            if (calculating)
            {
                Profiler.BeginSample("RealAntennas CompleteRebuild");
                tempWatch.Reset();
                tempWatch.Start();
                PrecomputeLateWatch.Reset();
                PrecomputeLateWatch.Start();
                calculating = false;
                Profiler.BeginSample("RealAntennas CompleteRebuild.UpdateNetwork");
                UpdateNetwork();
                Profiler.EndSample();
                PrecomputeLateWatch.Stop();
                LastUpdateUT = Planetarium.GetUniversalTime();
                Profiler.BeginSample("RealAntennas CompleteRebuild.BuildHomeTree");
                BuildHomeTree();
                Profiler.EndSample();
                // Links do not change until the next rebuild, so FindHome can use the tree during the post-update callbacks.
                homeTreeActive = true;
                try
                {
                    NetworkUpdateComplete.Fire();
                    PostUpdateNodes();
                    if (OnNetworkPostUpdate is Action)
                        OnNetworkPostUpdate();
                }
                finally
                {
                    homeTreeActive = false;
                }
                tempWatch.Stop();
                Profiler.EndSample();
                (RACommNetScenario.Instance as RACommNetScenario).metrics.AddMeasurement("Precompute LateRebuild", PrecomputeLateWatch.Elapsed.TotalMilliseconds);
                (RACommNetScenario.Instance as RACommNetScenario).metrics.AddMeasurement("Full LateRebuild", tempWatch.Elapsed.TotalMilliseconds);
            }
        }

        // Call this to abort a pre-computation pass that has already started.
        // Main use case is the node list changed during processing, ie a vessel was created or destroyed.
        public virtual void Abort()
        {
            calculating = false;
            precompute.Abort();
        }

        // Waits for any running jobs and frees all native memory. The network can't be used after this.
        public void Destroy()
        {
            calculating = false;
            precompute.Destroy();
        }

        protected override void UpdateNetwork()
        {
            //base.UpdateNetwork();
            precompute.Complete(this);
        }

        public void DoDisconnect(CommNode a, CommNode b) => Disconnect(a, b, true);

        public override void Rebuild()
        {
            // Base behavior is:
            // set isDirty = false
            // this?.OnNetworkPreUpdate()
            // this.PreUpdateNodes();
            // this.UpdateOccluders();
            // -- This far is fine

            // -- This should be deferred.
            // this.UpdateNetwork();
            // this.PostUpdateNodes();
            // this?.OnNetworkPostUpdate();

            if (!IsPaused)
            {
                Profiler.BeginSample("RealAntennas CommNetwork Rebuild");
                RebuildStopWatch.Reset();
                RebuildStopWatch.Start();
                base.Rebuild();
                RebuildStopWatch.Stop();
                Profiler.EndSample();
                (RACommNetScenario.Instance as RACommNetScenario).metrics.AddMeasurement("Rebuild", RebuildStopWatch.Elapsed.TotalMilliseconds);
            }
        }

        protected string CommNodeWalk()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{ModTag} CommNode walk");
            foreach (RACommNode item in nodes)
            {
                sb.Append($"\n{item.DebugToString()}");
            }
            return sb.ToStringAndRelease();
        }

        protected string CommLinkWalk()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{ModTag} CommLink walk");
            foreach (CommLink item in Links)
            {
                sb.Append($"\n{item}");
            }
            return sb.ToStringAndRelease();
        }

        public void Validate()
        {
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v.Connection?.Comm is RACommNode vcn && !nodes.Contains(vcn))
                {
                    Debug.LogWarning($"{ModTag} Vessel {v} had commnode {vcn} not in the node list.");
                    Add(vcn);
                }
            }
            CheckNodeConsistency();
        }

        public void CheckNodeConsistency()
        {
            foreach (var home in RACommNetScenario.EnabledStations)
            {
                home.CheckNodeConsistency();
            }

            foreach (var link in from RACommNode node in Nodes
                                 from link in node.Values
                                 where !(Nodes.Contains(link.start) && Nodes.Contains(link.end))
                                 select link)
            {
                Debug.LogWarning($"{ModTag} Found defunct link {link}");
            }
        }

        public void LogState()
        {
            Debug.Log(CommNodeWalk());
            Debug.Log(CommLinkWalk());
        }

        #region Pathfinding
        /*
        public override bool FindPath(CommNode start, CommPath path, CommNode end)
        {
            return base.FindPath(start, path, end);
        }
        */

        private struct PathHeapEntry
        {
            public double cost;
            public RACommNode node;
        }

        // Binary min-heap on cost. A node is pushed again each time its cost improves.
        // Stale entries are skipped when popped.
        private PathHeapEntry[] pathHeap = new PathHeapEntry[64];
        private int pathHeapCount;

        // A node is finalized in the current search when its pathfindVisitId equals this.
        private int pathfindSearchId;

        private void PathHeapPush(RACommNode node, double cost)
        {
            if (pathHeapCount == pathHeap.Length)
                Array.Resize(ref pathHeap, pathHeap.Length * 2);
            int i = pathHeapCount++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (pathHeap[parent].cost <= cost) break;
                pathHeap[i] = pathHeap[parent];
                i = parent;
            }
            pathHeap[i].cost = cost;
            pathHeap[i].node = node;
        }

        private PathHeapEntry PathHeapPop()
        {
            PathHeapEntry top = pathHeap[0];
            PathHeapEntry last = pathHeap[--pathHeapCount];
            pathHeap[pathHeapCount].node = null;
            int count = pathHeapCount;
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= count) break;
                if (child + 1 < count && pathHeap[child + 1].cost < pathHeap[child].cost)
                    child++;
                if (last.cost <= pathHeap[child].cost) break;
                pathHeap[i] = pathHeap[child];
                i = child;
            }
            if (count > 0)
                pathHeap[i] = last;
            return top;
        }

        private void PathHeapClear()
        {
            Array.Clear(pathHeap, 0, pathHeapCount);
            pathHeapCount = 0;
        }

        // Cached so that each pathfinding call does not allocate a new delegate.
        private static readonly Func<CommNode, CommNode, bool> isHomeFunc = IsHome;
        private static readonly Func<CommNode, CommNode, bool> isControlSourceFunc = IsControlSource;

        // Stock FindHome passes IsHome as a method group, which creates a new delegate every call.
        // We override it to pass cached static delegates instead.
        public override bool FindHome(CommNode from, CommPath path = null)
        {
            if (homeTreeActive && from is RACommNode start && start.homeTreeId == homeTreeId)
                return FindHomeFromTree(start, path);
            return FindClosestWhere(from, path, isHomeFunc) != null;
        }

        private bool homeTreeActive;
        private int homeTreeId;

        // Runs Dijkstra once backwards from all homes, so every node gets its shortest route home.
        // Gives the same costs as running FindClosestWhere(node, path, IsHome) for each node separately.
        private void BuildHomeTree()
        {
            PathfindWatch.Reset();
            PathfindWatch.Start();
            int treeId = ++homeTreeId;
            int visitId = ++pathfindSearchId;
            PathHeapClear();
            foreach (RACommNode n in Nodes)
            {
                n.homeTreeId = treeId;
                n.homeLink = null;
                n.homeNext = null;
                if (n.isHome)
                {
                    n.homeCost = 0;
                    PathHeapPush(n, 0);
                }
                else
                    n.homeCost = double.PositiveInfinity;
            }
            int minRelayTL = RACommNetScenario.minRelayTL;
            while (pathHeapCount > 0)
            {
                PathHeapEntry entry = PathHeapPop();
                RACommNode v = entry.node;
                if (v.pathfindVisitId == visitId || entry.cost > v.homeCost)
                    continue;
                v.pathfindVisitId = visitId;
                double vCost = v.homeCost;
                bool vIsHome = v.isHome;
                foreach (KeyValuePair<CommNode, CommLink> kvp in v)
                {
                    if (kvp.Key is RACommNode u && u.pathfindVisitId != visitId && kvp.Value is RACommLink link)
                    {
                        // Signal goes from u to v. v must be able to relay unless it is the destination.
                        bool fwd = link.start == u;
                        if ((fwd ? link.FwdAntennaRx : link.RevAntennaRx) is RealAntenna rxAntenna
                            && (vIsHome || rxAntenna.TechLevelInfo.Level >= minRelayTL))
                        {
                            double newCost = vCost + (fwd ? link.FwdCost : link.RevCost);
                            if (u.homeCost > newCost)
                            {
                                u.homeCost = newCost;
                                u.homeLink = link;
                                u.homeNext = v;
                                PathHeapPush(u, newCost);
                            }
                        }
                    }
                }
            }
            PathHeapClear();
            PathfindWatch.Stop();
            (RACommNetScenario.Instance as RACommNetScenario).metrics.AddMeasurement("HomeTree", PathfindWatch.Elapsed.TotalMilliseconds);
        }

        private bool FindHomeFromTree(RACommNode start, CommPath path)
        {
            path?.Clear();
            if (double.IsPositiveInfinity(start.homeCost))
                return false;
            if (path != null)
            {
                RACommNode n = start;
                while (n.homeNext != null)
                {
                    var link = new RACommLink();
                    link.Copy(n.homeLink);
                    // Path links point away from the start node.
                    if (link.a == n.homeNext)
                        link.SwapEnds();
                    path.Add(link);
                    n = n.homeNext;
                }
                path.UpdateFromPath();
            }
            return true;
        }

        public override bool FindClosestControlSource(CommNode from, CommPath path = null) =>
            FindClosestWhere(from, path, isControlSourceFunc) != null;

        public override CommNode FindClosestWhere(CommNode cnStart, CommPath path, Func<CommNode, CommNode, bool> where)
        {
            if (!(cnStart is RACommNode start && where != null))
                return base.FindClosestWhere(cnStart, path, where);
            Profiler.BeginSample("RealAntennas.FindClosestWhere");
            PathfindWatch.Reset();
            PathfindWatch.Start();
            path?.Clear();
            int searchId = ++pathfindSearchId;
            foreach (RACommNode racn in Nodes)
            {
                racn.bestCost = (racn == start) ? 0 : double.PositiveInfinity;
                racn.bestLink = null;
                racn.bestLinkNode = null;
            }
            PathHeapClear();
            PathHeapPush(start, 0);
            int minRelayTL = RACommNetScenario.minRelayTL;
            bool found = false;
            RACommNode candidate = null;
            while (!found && pathHeapCount > 0)
            {
                PathHeapEntry entry = PathHeapPop();
                RACommNode popped = entry.node;
                if (popped.pathfindVisitId == searchId || entry.cost > popped.bestCost)
                    continue;
                candidate = popped;
                candidate.pathfindVisitId = searchId;
                if (!(found = where(start, candidate)))
                {
                    double candidateCost = candidate.bestCost;
                    foreach (KeyValuePair<CommNode, CommLink> kvp in candidate)
                    {
                        if (kvp.Key is RACommNode node && node.pathfindVisitId != searchId && kvp.Value is RACommLink link)
                        {
                            bool fwd = link.start == candidate;
                            // Skip if the peer is unable to relay and is also not the destination
                            if ((fwd ? link.FwdAntennaRx : link.RevAntennaRx) is RealAntenna rxAntenna
                                && (rxAntenna.TechLevelInfo.Level >= minRelayTL || where(start, node)))
                            {
                                double newCost = candidateCost + (fwd ? link.FwdCost : link.RevCost);
                                if (node.bestCost > newCost)
                                {
                                    node.bestCost = newCost;
                                    node.bestLink = link;
                                    node.bestLinkNode = candidate;
                                    PathHeapPush(node, newCost);
                                }
                            }
                        }
                    }
                }
            }
            PathHeapClear();
            if (found)
            {
                CommNode n = candidate;
                while (n is RACommNode && n != start)
                {
                    var link = new RACommLink();
                    link.Copy(n.bestLink as RACommLink);
                    if (link.a == n)
                        link.SwapEnds();
                    path?.Insert(0, link);
                    n = n.bestLinkNode as RACommNode;
                }
                path?.UpdateFromPath();
            }
            PathfindWatch.Stop();
            (RACommNetScenario.Instance as RACommNetScenario).metrics.AddMeasurement("Pathfinding", PathfindWatch.Elapsed.TotalMilliseconds);
            Profiler.EndSample();
            return found ? candidate : null;
        }
        #endregion
    }
}
