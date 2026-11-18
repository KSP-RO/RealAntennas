using System.Collections.Generic;
using UnityEngine;

namespace RealAntennas.Targeting
{
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class AntennaTargetManager : MonoBehaviour
    {
        public static Dictionary<RealAntenna, GameObject> targets = new Dictionary<RealAntenna, GameObject>();
        public static Dictionary<RealAntenna, GameObject> guis = new Dictionary<RealAntenna, GameObject>();

        public void Awake()
        {
            targets.Clear();
            guis.Clear();
        }

        // Target GameObjects can be parented to CelestialBodies, which survive scene changes.
        public void OnDestroy()
        {
            foreach (GameObject go in targets.Values)
            {
                if (go != null)
                    Destroy(go);
            }
            targets.Clear();
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
