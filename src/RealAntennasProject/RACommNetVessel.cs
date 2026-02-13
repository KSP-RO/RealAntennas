using Expansions.Serenity.DeployedScience.Runtime;
using Experience.Effects;
using KSPCommunityFixes;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Profiling;

namespace RealAntennas
{
    public class RACommNetVessel : CommNet.CommNetVessel
    {
        protected const string ModTag = "[RealAntennasCommNetVessel]";
        readonly List<RealAntenna> antennaList = new List<RealAntenna>();
        readonly List<RealAntenna> inactiveAntennas = new List<RealAntenna>();
        readonly List<CommNet.ModuleProbeControlPoint> probeControlPoints = new List<CommNet.ModuleProbeControlPoint>();
        private PartResourceDefinition electricChargeDef;

        [KSPField(isPersistant = true)] public bool powered = true;

        public override IScienceDataTransmitter GetBestTransmitter() =>
            (IsConnected && Comm is RACommNode node && node.AntennaTowardsHome() is RealAntenna toHome) ? toHome.Parent : null;

        public override void OnNetworkPreUpdate()
        {
            base.OnNetworkPreUpdate();
            var cluster = GetDeployedScienceCluster(Vessel);
            if (cluster != null)
                powered = cluster.IsPowered;
            else if (Vessel.loaded && electricChargeDef != null)
            {
                Vessel.GetConnectedResourceTotals(electricChargeDef.id, out double amt, out double _);
                powered = amt > 0;
            }
        }

        public double IdlePowerDraw()
        {
            double ec = 0;
            if (!IsDeployedScienceCluster(Vessel))
            {
                foreach (RealAntenna ra in antennaList)
                {
                    ec += ra.IdlePowerDraw;
                }
                foreach (RealAntenna ra in inactiveAntennas)
                {
                    ec += ra.IdlePowerDraw;
                }
            }
            return ec;
        }

        protected override void OnStart()
        {
            if (vessel.vesselType == VesselType.Flag || vessel.vesselType <= VesselType.Unknown)
            {
                vessel.vesselModules.Remove(this);
                vessel.connection = null;
                Destroy(this);
            }
            else
            {
                comm = new RACommNode(transform)
                {
                    OnNetworkPreUpdate = new Action(OnNetworkPreUpdate),
                    OnNetworkPostUpdate = new Action(OnNetworkPostUpdate),
                    OnLinkCreateSignalModifier = new Func<CommNet.CommNode, double>(GetSignalStrengthModifier),
                    ParentVessel = Vessel,
                };
                (comm as RACommNode).RAAntennaList = DiscoverAntennas();
                DiscoverProbeControlPoints();
                vessel.connection = this;
                networkInitialised = false;
                if (CommNet.CommNetNetwork.Initialized)
                    OnNetworkInitialized();
                GameEvents.CommNet.OnNetworkInitialized.Add(OnNetworkInitialized);
                if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
                    GameEvents.onPlanetariumTargetChanged.Add(OnMapFocusChange);
                foreach (Part p in vessel.parts)
                {
                    List<PartModule> modules = p.FindModulesImplementingReadOnly<ModuleDeployablePart>();
                    foreach (ModuleDeployablePart mdp in modules)
                    {
                        mdp.OnMoving.Add(OnMoving);
                        mdp.OnStop.Add(OnStop);
                    }
                }

                overridePostUpdate = true;
                electricChargeDef = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
            }
        }

        protected override void Update()
        {
            if (vessel.loaded)
            {
                int count = vessel.Parts.Count;
                if (count != partCountCache)
                {
                    partCountCache = count;
                    Profiler.BeginSample("RA DiscoverPartModules");
                    FindCommandSources();
                    if (Comm is RACommNode)
                    {
                        DiscoverAntennas();
                    }
                    DiscoverProbeControlPoints();
                    Profiler.EndSample();
                }
                UpdateControlState();
            }
        }

        private void OnMoving(float f1, float f2) => DiscoverAntennas();
        private void OnStop(float f1) => DiscoverAntennas();

        protected override void OnDestroy()
        {
            GameEvents.CommNet.OnNetworkInitialized.Remove(OnNetworkInitialized);
            GameEvents.onPlanetariumTargetChanged.Remove(OnMapFocusChange);
            base.OnDestroy();
            comm?.Net.Remove(comm);
            comm = null;
            if (vessel) vessel.connection = null;
        }

        protected override void UpdateComm()
        {
            if (comm is RACommNode raComm)
            {
                raComm.name = gameObject.name;
                raComm.displayName = vessel.GetDisplayName();
                raComm.isControlSource = false;
                raComm.isControlSourceMultiHop = false;
                raComm.antennaRelay.power = raComm.antennaTransmit.power = 0.0;
                hasScienceAntenna = raComm.RAAntennaList.Count > 0;
                DetermineControl();
            }
        }

        private void DiscoverProbeControlPoints()
        {
            probeControlPoints.Clear();

            if (vessel.loaded)
            {
                foreach (Part part in vessel.Parts)
                {
                    if (part.FindModuleImplementingFast<CommNet.ModuleProbeControlPoint>() is CommNet.ModuleProbeControlPoint pcp &&
                        pcp.CanControl())
                    {
                        probeControlPoints.Add(pcp);
                    }
                }
            }
            else
            {
                int index = 0;
                foreach (ProtoPartSnapshot protoPartSnapshot in vessel.protoVessel.protoPartSnapshots)
                {
                    index++;
                    Part part = protoPartSnapshot.partInfo.partPrefab;
                    if (part.FindModuleImplementingFast<CommNet.ModuleProbeControlPoint>() is CommNet.ModuleProbeControlPoint pcp &&
                        pcp.CanControlUnloaded(protoPartSnapshot.FindModule(pcp, index)))
                    {
                        probeControlPoints.Add(pcp);
                    }
                }
            }
        }

        private int CountControllingCrew()
        {
            int numControl = 0;
            foreach (ProtoCrewMember crewMember in vessel.GetVesselCrew())
            {
                if (crewMember.HasEffect<FullVesselControlSkill>() && !crewMember.inactive)
                    ++numControl;
            }
            return numControl;
        }

        private void DetermineControl()
        {
            if (probeControlPoints.Count == 0) return;

            Profiler.BeginSample("RA DetermineControl");
            int numControl = CountControllingCrew();
            foreach (CommNet.ModuleProbeControlPoint pcp in probeControlPoints)
            {
                if (numControl >= pcp.minimumCrew || pcp.minimumCrew <= 0)
                    comm.isControlSource = true;
                if (pcp.multiHop)
                    comm.isControlSourceMultiHop = true;

                // Assume that control parts are closer to root than leaf nodes.
                // If both fields are true then no point in looking any further.
                if (comm.isControlSource && comm.isControlSourceMultiHop)
                    break;
            }
            Profiler.EndSample();
        }

        public List<RealAntenna> DiscoverAntennas()
        {
            antennaList.Clear();
            inactiveAntennas.Clear();
            (RACommNetScenario.Instance as RACommNetScenario)?.Network?.InvalidateCache();
            if (Vessel == null) return antennaList;
            if (Vessel.loaded)
            {
                foreach (Part part in vessel.parts)
                {
                    var moduleList = part.FindModulesImplementingReadOnly<ModuleRealAntenna>();
                    foreach (ModuleRealAntenna ant in moduleList)
                    {
                        if (ant.Condition == AntennaCondition.Enabled)
                        {
                            ant.RAAntenna.ParentNode = Comm;
                            if (DeployedLoaded(ant.part)) antennaList.Add(ant.RAAntenna);
                            else inactiveAntennas.Add(ant.RAAntenna);
                            ValidateAntennaTarget(ant.RAAntenna);
                        }
                    }
                }
                return antennaList;
            }

            if (Vessel.protoVessel != null)
            {
                foreach (ProtoPartSnapshot part in Vessel.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoPartModuleSnapshot snap in part.modules.Where(x => x.moduleName == ModuleRealAntenna.ModuleName))
                    {
                        bool _enabled = true;
                        string sState = null;
                        if (snap.moduleValues.TryGetValue(nameof(ModuleRealAntenna.Condition), ref sState))
                            _enabled = sState == AntennaCondition.Enabled.ToString();

                        // Doesn't get the correct PartModule if multiple, but the only impact is the name, which defaults to the part anyway.
                        if (_enabled && part.partInfo.partPrefab.FindModuleImplementingFast<ModuleRealAntenna>() is ModuleRealAntenna mra &&
                            mra.CanCommUnloaded(snap))
                        {
                            RealAntenna ra = new RealAntennaDigital(part.partPrefab.partInfo.title) { ParentNode = Comm, ParentSnapshot = snap };
                            ra.LoadFromConfigNode(snap.moduleValues);
                            if (DeployedUnloaded(part)) antennaList.Add(ra);
                            else inactiveAntennas.Add(ra);
                            ValidateAntennaTarget(ra);
                        }
                    }
                }
            }
            return antennaList;
        }
        private void ValidateAntennaTarget(RealAntenna ra)
        {
            if (ra.CanTarget && !(ra.Target?.Validate() == true))
                ra.Target = Targeting.AntennaTarget.LoadFromConfig(ra.SetDefaultTarget(), ra);
        }
        public static bool DeployedUnloaded(ProtoPartSnapshot part)
        {
            if (part.FindModule("ModuleDeployableAntenna") is ProtoPartModuleSnapshot deploySnap)
            {
                string deployState = string.Empty;
                deploySnap.moduleValues.TryGetValue("deployState", ref deployState);
                return deployState.Equals("EXTENDED");
            }
            return true;
        }
        public static bool DeployedLoaded(Part part) =>
            (part.FindModuleImplementingFast<ModuleDeployableAntenna>() is ModuleDeployableAntenna mda) ?
            mda.deployState == ModuleDeployablePart.DeployState.EXTENDED : true;

        private bool IsDeployedScienceCluster(Vessel v) => GetDeployedScienceCluster(v) != null;
        private DeployedScienceCluster GetDeployedScienceCluster(Vessel vessel)
        {
            DeployedScienceCluster cluster = null;
            if (vessel.vesselType == VesselType.DeployedScienceController)
            {
                var id = vessel.loaded ? vessel.rootPart.persistentId : vessel.protoVessel.protoPartSnapshots[0].persistentId;
                DeployedScience.Instance?.DeployedScienceClusters?.TryGetValue(id, out cluster);
            }
            return cluster;
        }
    }
}
