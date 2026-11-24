using System.Collections.Generic;
using UnityEngine;

namespace RealAntennas.Targeting
{
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class AntennaTargetManager : MonoBehaviour
    {
        public static Dictionary<RealAntenna, GameObject> targets = new Dictionary<RealAntenna, GameObject>();
        public static Dictionary<RealAntenna, GameObject> guis = new Dictionary<RealAntenna, GameObject>();
        private static readonly List<RealAntenna> antennasToRetarget = new List<RealAntenna>();

        public void Awake()
        {
            targets.Clear();
            guis.Clear();
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);
            GameEvents.onVesselTerminated.Add(OnVesselTerminated);
            GameEvents.onPartCouple.Add(OnPartCouple);
        }

        // Target GameObjects can be parented to CelestialBodies, which survive scene changes.
        public void OnDestroy()
        {
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(OnVesselTerminated);
            GameEvents.onPartCouple.Remove(OnPartCouple);

            foreach (GameObject go in targets.Values)
            {
                if (go != null)
                    Destroy(go);
            }
            targets.Clear();
        }

        // Vessel targets are parented to the target vessel and would be destroyed together with it.
        // These events fire before the vessel is destroyed, so the target can be moved elsewhere in time.
        private void OnVesselWillDestroy(Vessel v) => ResetTargetsOn(v);
        private void OnVesselRecovered(ProtoVessel pv, bool quick) => ResetTargetsOn(pv?.vesselRef);
        private void OnVesselTerminated(ProtoVessel pv) => ResetTargetsOn(pv?.vesselRef);

        private static void ResetTargetsOn(Vessel v)
        {
            if (v == null) return;
            foreach (RealAntenna ra in FindAntennasTargeting(v))
                ra.SetDefaultTarget();
        }

        // Docking destroys the Vessel of the docking part without calling Die().
        // Its parts join the target part's vessel, so follow that vessel instead.
        private void OnPartCouple(GameEvents.FromToAction<Part, Part> action)
        {
            Vessel from = action.from?.vessel;
            Vessel to = action.to?.vessel;
            if (from == null || to == null || from == to) return;

            var node = new ConfigNode(AntennaTarget.nodeName);
            node.AddValue("name", $"{AntennaTarget.TargetMode.Vessel}");
            node.AddValue("vesselId", to.id.ToString());
            foreach (RealAntenna ra in FindAntennasTargeting(from))
                ra.Target = AntennaTarget.LoadFromConfig(node, ra);
        }

        private static List<RealAntenna> FindAntennasTargeting(Vessel v)
        {
            antennasToRetarget.Clear();
            foreach (RealAntenna ra in targets.Keys)
            {
                if (ra.Target is AntennaTargetVessel tv && tv.vessel == v)
                    antennasToRetarget.Add(ra);
            }
            return antennasToRetarget;
        }

        public static AntennaTargetGUI AcquireGUI(RealAntenna ant)
        {
            if (!guis.ContainsKey(ant))
            {
                var go = new GameObject($"{ant.ParentNode?.name}:{ant.Name}:TargetGUI_GO");
                var gui = go.AddComponent<AntennaTargetGUI>();
                gui.name = $"{ant.ParentNode?.name}:{ant.Name}:TargetGUI";
                gui.antenna = ant;
                guis[ant] = go;
            }
            return guis[ant].GetComponent<AntennaTargetGUI>();
        }

        public static GameObject AcquireTarget(RealAntenna a)
        {
            if (!targets.ContainsKey(a))
                targets[a] = new GameObject($"{a.ParentNode?.name}:{a.Name}.Target");
            return targets[a];
        }

        public static void ReleaseTarget(RealAntenna a)
        {
            if (a != null && targets.TryGetValue(a, out var go))
            {
                targets.Remove(a);
                if (go != null)
                    Destroy(go);
            }
        }

        public static void Release(RealAntenna a, AntennaTargetGUI _)
        {
            if (guis.TryGetValue(a, out var go))
            {
                guis.Remove(a);
                go.DestroyGameObject();
            }
        }
    }
}
