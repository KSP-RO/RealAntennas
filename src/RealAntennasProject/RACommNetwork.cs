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

        public override CommNode Add(CommNode conn)
        {
            if (!(conn is RACommNode))
            {
                Debug.LogWarning($"{ModTag} Wrong commnode type, so ignoring.");
                return conn;
            }
            return base.Add(conn);
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
                               double RevMetric,
                               bool fwdValid = true,
                               bool revValid = true
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
            
            // Only average across directions that actually exist - otherwise
            // legitimately absent direction would halve the apparent rate.
            double avgRateForCost = (fwdValid && revValid) ? (FwdDataRate + RevDataRate) / 2 : (fwdValid ? FwdDataRate : RevDataRate);
            link.cost = link.CostFunc(avgRateForCost);
            link.FwdMetric = FwdMetric;
            link.RevMetric = RevMetric;
            if (FwdBestDataRate < FwdDataRate)
                Debug.LogWarning($"{ModTag} Detected actual rate {FwdDataRate} greater than expected max {FwdBestDataRate} for antennas {link.FwdAntennaTx} and {link.FwdAntennaRx}");

            // Same reasoning: Math.Min would zero out the signal strength of a
            // good one-directional link, since the missing side's metric is 0.
            double signalMetric = (fwdValid && revValid) ? Math.Min(link.FwdMetric, link.RevMetric) : (fwdValid ? link.FwdMetric : link.RevMetric);
            link.Update(signalMetric);
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






        // Data rate along a path to home that's usable for Science end to end
        // (see FindScienceCapableHome), not just the general best/control path.
        // If the best path crosses a Telemetry-only hop, this searches for a
        // fully Science-capable alternate route instead of reporting 0. Used by
        // the Kerbalism bridge, which bypasses ModuleRealAntenna's own duty check.
        public double MaxScienceDataRateToHome(RACommNode start)
        {
            if (start == null) return 0;
            CommPath path = new CommPath();
            if (!FindScienceCapableHome(start, path)) return 0;
            double data_rate = double.MaxValue;
            foreach (CommLink l in path)
            {
                RACommLink link = l.start[l.end] as RACommLink;
                double linkRate = link.start.Equals(l.start) ? link.FwdDataRate : link.RevDataRate;
                data_rate = Math.Min(data_rate, linkRate);
            }
            return data_rate == double.MaxValue ? 0 : data_rate;
        }

        public bool PathCanCarryScience(RACommNode start) => MaxScienceDataRateToHome(start) > 0;

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
                NetworkUpdateComplete.Fire();
                PostUpdateNodes();
                if (OnNetworkPostUpdate is Action)
                    OnNetworkPostUpdate();
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

        // findhome for recieve only craft
        public override bool FindHome(CommNode start, CommPath path)
        {
            if (base.FindHome(start, path))
                return true;
            if (currentPurpose != PathPurpose.General || !(start is RACommNode racn) || HasTelemetryCapableTransmitAntenna(racn))
                return false;

            foreach (var home in RACommNetScenario.EnabledStations)
            {
                if (!(home.Comm is RACommNode homeNode)) continue;
                CommPath reversePath = new CommPath();
                CommNode foundReverse;
                searchingFromHome = true;
                try
                {
                    foundReverse = FindClosestWhere(homeNode, reversePath, (s, c) => c == start);
                }
                finally
                {
                    searchingFromHome = false;
                }
                if (foundReverse != null)
                {
                    foreach (CommLink l in reversePath)
                        if (l is RACommLink racLink) racLink.SwapEnds();
                    reversePath.Reverse();
                    path?.Clear();
                    path?.AddRange(reversePath);
                    path?.UpdateFromPath();
                    return true;
                }
            }
            return false;
        }

        
        private bool searchingFromHome = false;

        // the fallback should only trigger when the craft has
        // no way to originate a CONTROL-carrying transmission
        private static bool HasTelemetryCapableTransmitAntenna(RACommNode node) =>
            node.RAAntennaList != null && node.RAAntennaList.Any(ra => ra.CanTransmitRF && ra.CanHandleTelemetry);

        #region Pathfinding
        /*
        public override bool FindPath(CommNode start, CommPath path, CommNode end)
        {
            return base.FindPath(start, path, end);
        }
        */

        
        public enum PathPurpose { General, ScienceCapable }
        private PathPurpose currentPurpose = PathPurpose.General;

        
        public bool FindScienceCapableHome(RACommNode start, CommPath path)
        {
            currentPurpose = PathPurpose.ScienceCapable;
            try
            {
                return FindHome(start, path);
            }
            finally
            {
                currentPurpose = PathPurpose.General;
            }
        }

        private readonly HashSet<RACommNode> sptSet = new HashSet<RACommNode>();
        private readonly List<RACommNode> pathSortList = new List<RACommNode>();
        public override CommNode FindClosestWhere(CommNode cnStart, CommPath path, Func<CommNode, CommNode, bool> where)
        {
            if (!(cnStart is RACommNode start && where != null))
                return base.FindClosestWhere(cnStart, path, where);
            Profiler.BeginSample("RealAntennas.FindClosestWhere");
            PathfindWatch.Reset();
            PathfindWatch.Start();
            path?.Clear();
            sptSet.Clear();
            pathSortList.Clear();
            float minControlUplinkRate = currentPurpose == PathPurpose.General
                ? HighLogic.CurrentGame.Parameters.CustomParams<RAParameters>().MinControlUplinkBitRate
                : 0f;
            foreach (RACommNode racn in Nodes)
            {
                racn.bestCost = (racn == start) ? 0 : double.PositiveInfinity;
                racn.bestLink = null;
                racn.bestLinkNode = null;
            }
            pathSortList.Add(start);
            bool found = false;
            RACommNode candidate = null;
            while (!found && pathSortList.Count > 0)
            {
                pathSortList.Sort((x, y) => x.bestCost.CompareTo(y.bestCost));
                candidate = pathSortList.First();
                pathSortList.RemoveAt(0);
                sptSet.Add(candidate);
                if (!(found = where(start, candidate)))
                {
                    foreach (KeyValuePair<CommNode, CommLink> kvp in candidate)
                    {
                        if (kvp.Key is RACommNode node && kvp.Value is RACommLink link && !sptSet.Contains(node))
                        {
                            bool forward = link.start == candidate;
                            RealAntenna txAntenna = forward ? link.FwdAntennaTx : link.RevAntennaTx;
                            RealAntenna rxAntenna = forward ? link.FwdAntennaRx : link.RevAntennaRx;
                            
                            double uplinkRateThisHop = searchingFromHome
                                ? (forward ? link.FwdDataRate : link.RevDataRate)
                                : (forward ? link.RevDataRate : link.FwdDataRate);

                            
                            bool relayOk = rxAntenna is RealAntenna
                                && (rxAntenna.TechLevelInfo.Level >= RACommNetScenario.minRelayTL || where(start, node));
                            bool scienceOk = currentPurpose != PathPurpose.ScienceCapable
                                || (txAntenna is RealAntenna && rxAntenna is RealAntenna && txAntenna.CanHandleScience && rxAntenna.CanHandleScience);
                            
                            bool telemetryOk = currentPurpose != PathPurpose.General
                                || (txAntenna is RealAntenna && rxAntenna is RealAntenna && txAntenna.CanHandleTelemetry && rxAntenna.CanHandleTelemetry);
                            bool uplinkOk = minControlUplinkRate <= 0 || uplinkRateThisHop >= minControlUplinkRate;

                            if (relayOk && scienceOk && telemetryOk && uplinkOk)
                            {
                                double cost = forward ? link.FwdCost : link.RevCost;
                                if (node.bestCost > candidate.bestCost + cost)
                                {
                                    node.bestCost = candidate.bestCost + cost;
                                    node.bestLink = link;
                                    node.bestLinkNode = candidate;
                                }
                                pathSortList.AddUnique(node);
                            }
                        }
                    }
                }
            }
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
