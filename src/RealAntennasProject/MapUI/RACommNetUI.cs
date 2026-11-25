using CommNet;
using RealAntennas.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Some Icons made by <a href="https://www.flaticon.com/<?=_('authors/')?>smalllikeart" title="smalllikeart"> smalllikeart</a>

namespace RealAntennas.MapUI
{
    public class RACommNetUI : CommNet.CommNetUI
    {
        private const string icon = "RealAntennas/radio-antenna";
        private const int numCirclePts = 180;

        public Color colorToTarget = XKCDColors.BananaYellow;
        public Color color3dB = XKCDColors.BarbiePink;
        public Color color10dB = XKCDColors.Lavender;

        public readonly Dictionary<string, SiteNode> groundStationSiteNodes = new Dictionary<string, SiteNode>();

        private readonly List<Vector3d> targetPoints = new List<Vector3d>();
        private readonly List<Vector3> targetPoints_out = new List<Vector3>();
        private readonly List<Vector3d> cone3Points = new List<Vector3d>();
        private readonly List<Vector3> cone3Points_out = new List<Vector3>();
        private readonly List<Vector3d> cone10Points = new List<Vector3d>();
        private readonly List<Vector3> cone10Points_out = new List<Vector3>();
        private readonly List<CommLink> commLinkList = new List<CommLink>();
        private readonly List<CommLink> _scratchLinkList = new List<CommLink>();
        private readonly Cone cone3 = new Cone();
        private readonly Cone cone10 = new Cone();
        private RendererPool solidLinkPool;
        private RendererPool dottedLinkPool;
        private RendererPool conePool;

        // Renderers are handed out in draw order every frame and returned all at once at the start of the next one.
        // The pool only grows to the most lines drawn in a single frame and is destroyed with this component.
        private sealed class RendererPool
        {
            private readonly List<LineRenderer> renderers = new List<LineRenderer>();
            private readonly Func<LineRenderer> create;
            private int used;

            public RendererPool(Func<LineRenderer> create) => this.create = create;

            public LineRenderer Get()
            {
                if (used == renderers.Count)
                    renderers.Add(create());
                else if (renderers[used] == null)
                    renderers[used] = create();
                return renderers[used++];
            }

            public void ReturnAll()
            {
                for (int i = 0; i < used; i++)
                {
                    if (renderers[i] != null)
                        renderers[i].enabled = false;
                }
                used = 0;
            }

            public void GatherEnabled(List<GameObject> res)
            {
                for (int i = 0; i < used; i++)
                {
                    if (renderers[i] != null && renderers[i].enabled)
                        res.Add(renderers[i].gameObject);
                }
            }

            public void SetLayer(int layer)
            {
                foreach (LineRenderer rend in renderers)
                {
                    if (rend != null)
                        rend.gameObject.layer = layer;
                }
            }

            public void DestroyAll()
            {
                foreach (LineRenderer rend in renderers)
                {
                    if (rend != null)
                        Destroy(rend.gameObject);
                }
                renderers.Clear();
                used = 0;
            }
        }


        public enum DrawConesMode { None, Cone2D, Cone3D };
        public enum RadioPerspective { Transmit, Receive };

        protected override void Awake()
        {
            base.Awake();
            solidLinkPool = new RendererPool(() => CreateRenderer("LinkLineRenderer"));
            dottedLinkPool = new RendererPool(() => CreateRenderer("LinkLineRenderer", dotted: true));
            conePool = new RendererPool(() => CreateRenderer("ConeLineRenderer"));
        }

        protected override void Start()
        {
            base.Start();
            if (!(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.TRACKSTATION))
                return;

            if (MapView.fetch is MapView)
            {
                foreach (RACommNetHome home in RACommNetScenario.EnabledStations)
                {
                    ConstructSiteNode(home);
                }
            }
            RATelemetryUpdate.Install();
        }
        public void ConstructSiteNode(RACommNetHome home) {
            Debug.LogFormat($"[RACommNetUI] Adding GroundStationSiteNode for {home.displaynodeName}");
            MapUI.GroundStationSiteNode gs = new MapUI.GroundStationSiteNode(home.Comm);
            SiteNode siteNode = SiteNode.Spawn(gs);
            Texture2D stationTexture = (GameDatabase.Instance.GetTexture(home.icon, false) is Texture2D tex) ? tex : GameDatabase.Instance.GetTexture(icon, false);
            // fetch default texture if conditional fails
            siteNode.wayPoint.node.SetIcon(Sprite.Create(stationTexture, new Rect(0, 0, stationTexture.width, stationTexture.height), new Vector2(0.5f, 0.5f), 100f));
            siteNode.wayPoint.node.OnUpdateVisible += home.OnUpdateVisible;
            groundStationSiteNodes[home.nodeName] = siteNode; // skopos still needs these
        }

        // If either one of these lists is nonempty, the links that are shown
        // are exactly those in OverrideShownLinks, and the beamwidth cones are
        // exactly those of the nodes in OverrideShownCones, regardless of the
        // UI mode.
        // The list is used and cleared by this class at the BetterLateThanNever
        // timing of LateUpdate.
        public List<CommLink> OverrideShownLinks { get; private set; } = new List<CommLink>();
        public List<RACommNode> OverrideShownCones { get; private set; } = new List<RACommNode>();

        private void GatherLinkLines(List<CommLink> linkList)
        {
            var settings = RACommNetScenario.MapUISettings;
            foreach (RACommLink link in linkList)
            {
                bool dotted = link.FwdAntennaTx.TechLevelInfo.Level < RACommNetScenario.minRelayTL ||
                              link.FwdAntennaRx.TechLevelInfo.Level < RACommNetScenario.minRelayTL;
                LineRenderer renderer = dotted ? dottedLinkPool.Get() : solidLinkPool.Get();
                renderer.enabled = true;
                Vector3d scaledStart = ScaledSpace.LocalToScaledSpace(link.start.precisePosition);
                Vector3d scaledEnd = ScaledSpace.LocalToScaledSpace(link.end.precisePosition);
                Camera cam = PlanetariumCamera.Camera;
                Vector3 camPos = cam.transform.position;
                float dStart = (float) Vector3d.Distance(camPos, scaledStart);
                float dEnd = (float) Vector3d.Distance(camPos, scaledEnd);
                renderer.startWidth = dStart * settings.lineScaleWidth / 1000;
                renderer.endWidth = dEnd * settings.lineScaleWidth / 1000;
                Color startColor = (settings.radioPerspective == RadioPerspective.Transmit) ? LinkColor(link.FwdMetric) : LinkColor(link.RevMetric);
                Color endColor = (settings.radioPerspective == RadioPerspective.Transmit) ? LinkColor(link.RevMetric) : LinkColor(link.FwdMetric);
                SetColorGradient(renderer, startColor, endColor);
                renderer.positionCount = 2;
                renderer.SetPositions(new Vector3[] { scaledStart, scaledEnd });
            }
        }

        private void LocalToScaledSpace(List<Vector3d> local, List<Vector3> scaled)
        {
            scaled.Clear();
            foreach (var x in local)
                scaled.Add(ScaledSpace.LocalToScaledSpace(x));
        }

        public void GatherAntennaCones(RACommNode node)
        {
            if (node == null || node.RAAntennaList.Count == 0) return;
            var settings = RACommNetScenario.MapUISettings;

            foreach (RealAntenna ra in node.RAAntennaList)
            {
                if (ra.CanTarget && ra.Target != null)
                {
                    LineRenderer targetRenderer = conePool.Get();
                    LineRenderer cone3Renderer = conePool.Get();
                    LineRenderer cone10Renderer = conePool.Get();

                    Vector3d axis = ra.ToTarget;
                    double len = Math.Min(1e8f, axis.magnitude);
                    axis.Normalize();
                    axis *= len;
                    cone10.Init(node.precisePosition, axis, Vector3.up, ra.Beamwidth);
                    cone3.Init(node.precisePosition, axis, Vector3.up, ra.Beamwidth / 2);

                    targetPoints.Clear();
                    cone3Points.Clear();
                    cone10Points.Clear();

                    Camera cam = PlanetariumCamera.Camera;
                    Vector3 camPos = cam.transform.position;

                    if (settings.drawTarget)
                    {
                        targetPoints.Add(node.precisePosition);
                        targetPoints.Add(node.precisePosition + ra.ToTarget);
                        LocalToScaledSpace(targetPoints, targetPoints_out);
                        float dStart = Vector3.Distance(camPos, targetPoints_out[0]);
                        float dEnd = Vector3.Distance(camPos, targetPoints_out[1]);
                        targetRenderer.startWidth = dStart * settings.lineScaleWidth / 1000;
                        targetRenderer.endWidth = dEnd * settings.lineScaleWidth / 1000;
                        SetColorGradient(targetRenderer, colorToTarget, colorToTarget, settings.coneOpacity, settings.coneOpacity);
                    }

                    if (settings.drawCone3 && settings.drawConesMode != DrawConesMode.None)
                    {
                        cone3Points.Add(cone3.end1);
                        cone3Points.Add(cone3.vertex);
                        if (settings.drawConesMode == DrawConesMode.Cone3D)
                            MakeCircles(cone3Points, cone3, settings.coneCircles);
                        else
                            cone3Points.Add(cone3.end2);
                        LocalToScaledSpace(cone3Points, cone3Points_out);
                        float dStart = Vector3.Distance(camPos, ScaledSpace.LocalToScaledSpace(cone3.vertex));
                        cone3Renderer.startWidth = dStart * settings.lineScaleWidth / 1000;
                        cone3Renderer.endWidth = dStart * settings.lineScaleWidth / 1000;
                        SetColorGradient(cone3Renderer, color3dB, color3dB, settings.coneOpacity, settings.coneOpacity);
                    }

                    if (settings.drawCone10 && settings.drawConesMode != DrawConesMode.None)
                    {
                        cone10Points.Add(cone10.end1);
                        cone10Points.Add(cone10.vertex);
                        if (settings.drawConesMode == DrawConesMode.Cone3D)
                            MakeCircles(cone10Points, cone10, settings.coneCircles);
                        else
                            cone10Points.Add(cone10.end2);

                        LocalToScaledSpace(cone10Points, cone10Points_out);
                        float dStart = Vector3.Distance(camPos, ScaledSpace.LocalToScaledSpace(cone10.vertex));
                        cone10Renderer.startWidth = dStart * settings.lineScaleWidth / 1000;
                        cone10Renderer.endWidth = dStart * settings.lineScaleWidth / 1000;
                        SetColorGradient(cone10Renderer, color10dB, color10dB, settings.coneOpacity, settings.coneOpacity);
                    }

                    targetRenderer.positionCount = targetPoints.Count;
                    targetRenderer.SetPositions(targetPoints_out.ToArray());
                    targetRenderer.enabled = settings.drawTarget;

                    cone3Renderer.positionCount = cone3Points.Count;
                    cone3Renderer.SetPositions(cone3Points_out.ToArray());
                    cone3Renderer.enabled = settings.drawCone3 && settings.drawConesMode != DrawConesMode.None;

                    cone10Renderer.positionCount = cone10Points.Count;
                    cone10Renderer.SetPositions(cone10Points_out.ToArray());
                    cone10Renderer.enabled = settings.drawCone10 && settings.drawConesMode != DrawConesMode.None;
                }
            }
        }

        private Color LinkColor(double metric)
        {
            if (metric <= 0) return Color.black;
            return Color.Lerp(Color.red, Color.green, Convert.ToSingle(metric));
        }

        private void MakeCircles(List<Vector3d> points, Cone cone, int numCircles)
        {
            if (numCircles == 0)        // No circles, just complete the cone.
                points.Add(cone.end2);

            for (int circ = 1; circ <= numCircles; circ++)
            {
                float scale = 1f * circ / numCircles;
                // Traverse to next circle
                points.Add(Vector3d.Lerp(cone.vertex, cone.end2, scale));
                DrawCircle(points,
                            Vector3d.Lerp(cone.vertex, cone.Midpoint, scale),
                            cone.Midpoint - cone.vertex,
                            Vector3d.Lerp(cone.vertex, cone.end2, scale),
                            360,
                            numCirclePts);
            }
        }

        // Given a circle center and its normal, draw an arc through <angle> degrees starting at startPoint on the circle using numPoints vertices
        // Direction is right-hand clockwise (normal cross startPoint)
        private void DrawCircle(List<Vector3d> points, Vector3d center, Vector3d normal, Vector3d startPoint, float angle, int numPoints)
        {
            normal.Normalize();
            Vector3 start = startPoint - center;
            Vector3 startDir = start.normalized;
            Vector3 rotateDir = Vector3.Cross(normal, startDir);
            float radius = start.magnitude;

            float step = 4 * (angle / 360) / numPoints;
            for (int i = 0; i < numPoints; i++)
            {
                Vector3 cur = center + radius * Vector3.SlerpUnclamped(startDir, rotateDir, i * step);
                points.Add(cur);
            }
            points.Add(startPoint);
        }

        public class Cone
        {
            public Vector3d vertex, end1, end2;
            public Cone() : this(Vector3d.zero, Vector3d.up, Vector3d.right) { }
            public Cone(Vector3d vertex, Vector3d end1, Vector3d end2)
            {
                this.vertex = vertex;
                this.end1 = end1;
                this.end2 = end2;
            }
            public Cone(Vector3d vertex, Vector3d axis, Vector3d normal, double angle)
            {
                Init(vertex, axis, normal, angle);
            }
            public void Init(Vector3d vertex, Vector3d axis, Vector3d normal, double angle)
            {
                this.vertex = vertex;
                Vector3d perp = Vector3.Cross(axis, normal).normalized;
                float angleRadians = Convert.ToSingle(angle * Math.PI / 180);
                end1 = vertex + Vector3.RotateTowards(axis, perp, angleRadians, 0);
                end2 = vertex + Vector3.RotateTowards(axis, perp, -angleRadians, 0);
            }
            public Vector3d Midpoint => (end1 + end2) / 2;
        }

        // Runs in LateUpdate at BetterLateThanNever (Timing5, 8008).
        protected override void UpdateDisplay()
        {
            //base.UpdateDisplay();
            if (CommNetNetwork.Instance == null) return;
            ReturnAllRenderers();
            if (CommNetUI.Mode == CommNetUI.DisplayMode.None) return;
            var settings = RACommNetScenario.MapUISettings;
            if (this.draw3dLines != MapView.Draw3DLines)
            {
                this.draw3dLines = MapView.Draw3DLines;
                ResetRendererLayer(MapView.Draw3DLines);
            }
            colorToTarget.a = settings.coneOpacity;
            color3dB.a = settings.coneOpacity;
            color10dB.a = settings.coneOpacity;

            if (RACommNetScenario.RACN is RACommNetwork commNet)
            {
                if (OverrideShownLinks.Count > 0 || OverrideShownCones.Count > 0)
                {
                    GatherLinkLines(OverrideShownLinks);
                    foreach (RACommNode node in OverrideShownCones)
                    {
                        GatherAntennaCones(node);
                    }
                    OverrideShownLinks.Clear();
                    OverrideShownCones.Clear();
                    return;
                }
                if (CommNetUI.Mode == CommNetUI.DisplayMode.Network)
                {
                    // Every link is in the Values of both its end nodes, so draw links from the network-wide list.
                    GatherLinkLines(commNet.Links);
                    foreach (RACommNode node in commNet.Nodes)
                        GatherAntennaCones(node);
                }
                if (vessel?.Connection?.Comm is RACommNode commNode &&
                    vessel?.Connection?.ControlPath is CommPath commPath)
                {
                    switch (CommNetUI.Mode)
                    {
                        case DisplayMode.FirstHop:
                            if (commPath.FirstOrDefault() is CommLink cl)
                            {
                                commLinkList.Clear();
                                commLinkList.Add(cl.start == commNode ? cl.start[cl.end] : cl.end[cl.start]);
                                GatherLinkLines(commLinkList);
                            }
                            GatherAntennaCones(commNode);
                            break;
                        case CommNetUI.DisplayMode.VesselLinks:
                            _scratchLinkList.Clear();
                            _scratchLinkList.AddRange(commNode.Values);
                            GatherLinkLines(_scratchLinkList);
                            GatherAntennaCones(commNode);
                            break;
                        case CommNetUI.DisplayMode.Path:
                            if (commPath.Count == 0)
                            {
                                _scratchLinkList.Clear();
                                _scratchLinkList.AddRange(commNode.Values);
                                GatherLinkLines(_scratchLinkList);
                                GatherAntennaCones(commNode);
                            }
                            foreach (CommLink link in commPath)
                            {
                                if (link.start.TryGetValue(link.end, out var x))
                                {
                                    commLinkList.Clear();
                                    commLinkList.Add(x);
                                    GatherLinkLines(commLinkList);
                                    GatherAntennaCones(link.start as RACommNode);
                                }
                                else
                                {
                                    Debug.LogWarning($"[RealAntennas.MapUI] {commNode} has broken link {link}");
                                    commNet.DoDisconnect(link.start, link.end);
                                }
                            }
                            break;
                    }
                }
            }
        }

        private LineRenderer CreateRenderer(string name, bool dotted = false)
        {
            LineRenderer rend = new GameObject(name).AddComponent<LineRenderer>();
            InitializeRenderer(rend, dotted);
            return rend;
        }

        private void InitializeRenderer(LineRenderer rend, bool dotted = false)
        {
            rend.material = dotted ? MapView.DottedLinesMaterial : lineMaterial;
            rend.material.SetTexture("_MainTex", dotted ? MapView.DottedLinesMaterial.mainTexture : lineTexture);
            rend.textureMode = dotted ? LineTextureMode.Tile : LineTextureMode.Stretch;
            ResetRendererLayer(rend, MapView.Draw3DLines);
            rend.receiveShadows = false;
            rend.generateLightingData = false;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.useWorldSpace = true;
            rend.startWidth = 12;
            rend.endWidth = 12;
            rend.startColor = Color.green;
            rend.endColor = Color.red;
            rend.enabled = false;
        }

        public static void SetColorGradient(LineRenderer rend, Color startColor, Color endColor, float startAlpha=1, float endAlpha=1)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(startColor, 0.0f), new GradientColorKey(endColor, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(startAlpha, 0.0f), new GradientAlphaKey(endAlpha, 1.0f) }
            );
            rend.colorGradient = gradient;
        }

        public override void SwitchMode(int step)
        {
            base.SwitchMode(step);
        }

        public override void Hide()
        {
            ReturnAllRenderers();
            base.Hide();
        }

        protected override void OnDestroy()
        {
            solidLinkPool.DestroyAll();
            dottedLinkPool.DestroyAll();
            conePool.DestroyAll();
            base.OnDestroy();
        }

        #region Renderer Pool Methods
        private void ReturnAllRenderers()
        {
            solidLinkPool.ReturnAll();
            dottedLinkPool.ReturnAll();
            conePool.ReturnAll();
        }

        public void GatherAllLinkRenderers(List<GameObject> res)
        {
            solidLinkPool.GatherEnabled(res);
            dottedLinkPool.GatherEnabled(res);
        }

        private void ResetRendererLayer(bool mode3D)
        {
            int layer = RendererLayer(mode3D);
            solidLinkPool.SetLayer(layer);
            dottedLinkPool.SetLayer(layer);
            conePool.SetLayer(layer);
        }

        private void ResetRendererLayer(LineRenderer rend, bool mode3D)
        {
            rend.gameObject.layer = RendererLayer(mode3D);
        }

        private static int RendererLayer(bool mode3D) => mode3D ? 31 : 10;

        #endregion
    }
}
