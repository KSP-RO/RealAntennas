using KSP.UI.Screens.Mapview;
using System;
using UnityEngine;

namespace RealAntennas.MapUI
{
    public class GroundStationSiteNode : ISiteNode
    {
        public readonly RACommNode node;

        public GroundStationSiteNode(RACommNode node)
        {
            this.node = node;
        }

        public string GetName() => node.name;
        public Transform GetWorldPos() => node.transform;

        public void UpdateNodeCaption(MapNode mn, MapNode.CaptionData data)
        {
            data.captionLine1 = $"{node.displayName}";
            data.captionLine2 = RATools.PrettyPrint(node.RAAntennaList);
            data.captionLine3 = FormatLatLon();
        }

        private string FormatLatLon()
        {
            CelestialBody body = node.ParentBody;
            if (body == null) return string.Empty;
            body.GetLatLonAlt(node.position, out double lat, out double lon, out _);
            lon = ((lon + 540d) % 360d) - 180d;
            return $"{Math.Abs(lat):F2}° {(lat >= 0 ? "N" : "S")}, {Math.Abs(lon):F2}° {(lon >= 0 ? "E" : "W")}";
        }
    }
}
