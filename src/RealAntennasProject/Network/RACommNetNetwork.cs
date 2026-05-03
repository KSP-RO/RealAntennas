using UnityEngine;
using CommNet;
using UnityEngine.Profiling;

namespace RealAntennas.Network
{
    /// <summary>
    /// Extend the functionality of the KSP's CommNetNetwork (co-primary model in the Model–view–controller sense; CommNet<> is the other co-primary one)
    /// </summary>
    public class RACommNetNetwork : CommNetNetwork
    {
        private const string ModTag = "[RACommNetNetwork]";
        // Set when the node topology has changed (vessels created/destroyed).
        // Requires Validate() + full precompute.Initialize() before next rebuild.
        private bool topologyDirty = false;

        // Set when antennas on existing nodes have changed (e.g. parts staged,
        // antennas deployed/retracted) but no nodes were added or removed.
        // Requires only precompute.UpdateAllAntennas() — no re-pairing needed.
        private bool antennaDirty = false;

        // True while ResetNetwork() is firing OnNetworkInitialized and vessels
        // are registering their CommNodes. InvalidateCache() is suppressed during
        // this window — the single topologyDirty flag set at the end of
        // ResetNetwork() covers the full Initialize() once all nodes are present.
        private bool _initializing = false;

        protected override void Awake()
        {
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v.Connection != null && !(v.Connection is RACommNetVessel ra))
                {
                    Debug.Log($"{ModTag} Rebuilding CommVessel on {v}.  (Was {v.Connection} of type {v.Connection.GetType()})");
                    CommNetVessel temp = v.Connection;
                    v.Connection = v.Connection.gameObject.AddComponent<RACommNetVessel>();
                    Destroy(temp);
                }
            }
            // Not sure why the base singleton Instance check is killing itself.  (Instance != this?)
            CommNetNetwork.Instance = this;
            if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
                GameEvents.onPlanetariumTargetChanged.Add(OnMapFocusChange);
            GameEvents.OnGameSettingsApplied.Add(ResetNetwork);
            GameEvents.onVesselCreate.Add(VesselCreateHandler);
            GameEvents.onVesselDestroy.Add(VesselDestroyHandler);
            //CommNetNetwork.Reset();       // Don't call this way, it will invoke the parent class' ResetNetwork()
        }

        // Called from RACommNetVessel.DiscoverAntennas() when antenna list on an
        // existing node changes. This does NOT add or remove CommNodes, so a full
        // re-pair is not needed — only antenna data needs refreshing.
        // Suppressed during ResetNetwork() since the single topologyDirty set at
        // the end of that method covers everything once all nodes are registered.
        public void InvalidateCache()
        {
            if (!_initializing)
                antennaDirty = true;
        }

        private void VesselCreateHandler(Vessel v) => topologyDirty = true;

        private void VesselDestroyHandler(Vessel v)
        {
            // The node list is about to shrink. Mark topology dirty so we
            // re-pair after the node is gone.
            topologyDirty = true;
            if (v?.Connection?.Comm is RACommNode node)
            {
                var l = new System.Collections.Generic.List<CommLink>();
                foreach (var link in node.Values)
                    l.Add(link);
                foreach (var link in l)
                    (CommNet as RACommNetwork).DoDisconnect(link.start, link.end);
            }
        }

        protected virtual void Start()
        {
            if (HighLogic.LoadedSceneHasPlanetarium)
            {
                TimingManager.UpdateAdd(TimingManager.TimingStage.ObscenelyEarly, UpdateEarly);
            }
            ResetNetwork();
        }

        internal new void ResetNetwork()
        {
            Debug.Log($"{ModTag} ResetNetwork()");

            CommNet = new RACommNetwork();
            Debug.Log($"{ModTag} Firing onNetworkInitialized()");
            // Suppress InvalidateCache() during Fire() — vessels registering their
            // CommNodes will call DiscoverAntennas() which calls InvalidateCache(),
            // but we don't want N antennaDirty triggers here. Instead, set
            // topologyDirty once after all vessels have registered so UpdateEarly()
            // performs a single Validate() + Initialize() on the next frame when
            // the node list is complete.
            _initializing = true;
            GameEvents.CommNet.OnNetworkInitialized.Fire();
            _initializing = false;
            Debug.Log($"{ModTag} Completed onNetworkInitialized()");
            topologyDirty = true;
        }

        protected override void Update()
        {
            var RACN = commNet as RACommNetwork;
            if (topologyDirty || antennaDirty)
                RACN.Abort();
            else
                RACN.CompleteRebuild();
        }

        protected virtual void UpdateEarly()
        {
            Profiler.BeginSample("RA UpdateEarly");
            var RACN = commNet as RACommNetwork;
            if (topologyDirty || antennaDirty)
                if (topologyDirty)
                {
                    // Node list may have changed. Validate() checks FlightGlobals for
                    // any CommNodes that are missing from the network and adds them.
                    // Initialize() must always follow Validate() here so the pair table
                    // is built from the fully-corrected node list.
                    RACN.Validate();
                    RACN.precompute.Initialize();
                    topologyDirty = false;
                    antennaDirty = false;   // Initialize() already captured current antenna state
                }
                else if (antennaDirty)
                {
                    // Node list is unchanged; only antenna properties (position, target
                    // direction, etc.) have been updated on existing nodes.
                    // UpdateAllAntennas() refreshes the precomputed antenna data in-place
                    // without rebuilding the O(N^2) pair table.
                    //
                    // However: Validate() is called here as a safety net. If a node was
                    // added between the last topology check and now (e.g. a vessel whose
                    // CommNode registered after VesselCreateHandler fired), Validate()
                    // will return true and we escalate to a full Initialize() so that
                    // vessel is not silently absent from the pair table.
                    bool unexpectedNodes = RACN.Validate();
                    if (unexpectedNodes)
                    {
                        Debug.LogWarning($"{ModTag} Validate() found unexpected new nodes during antenna-only refresh. Escalating to full Initialize().");
                        RACN.precompute.Initialize();
                    }
                    else
                    {
                        RACN.precompute.UpdateAllAntennas();
                    }
                    antennaDirty = false;
                }

            double interval = System.Math.Min(packedInterval, unpackedInterval);
            // double tm = Time.timeSinceLevelLoad;
            double tm = Planetarium.GetUniversalTime();
            graphDirty |= tm > prevUpdate + interval;
            bool compute = graphDirty || queueRebuild || commNet.IsDirty;
            RACN.StartRebuild(compute);
            if (compute)
            {
                prevUpdate = tm;
                graphDirty = queueRebuild = false;
            }
            Profiler.EndSample();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            GameEvents.onPlanetariumTargetChanged.Remove(OnMapFocusChange);
            GameEvents.OnGameSettingsApplied.Remove(ResetNetwork);
            GameEvents.onVesselCreate.Remove(VesselCreateHandler);
            GameEvents.onVesselDestroy.Remove(VesselDestroyHandler);
            TimingManager.UpdateRemove(TimingManager.TimingStage.ObscenelyEarly, UpdateEarly);
            (CommNet as RACommNetwork).precompute.Destroy();
        }
    }
}
