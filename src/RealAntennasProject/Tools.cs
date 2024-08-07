﻿using System.Collections.Generic;
using CommNet;
using System.Linq;
using Unity.Mathematics;
using System.Text;

namespace RealAntennas
{
    public class RATools : object
    {
        private static bool? _RP1Found = null;
        public static bool RP1Found
        {
            get
            {
                if (!_RP1Found.HasValue)
                {
                    var assembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "RP0")?.assembly;
                    _RP1Found = assembly != null;
                }
                return _RP1Found.Value;
            }
        }

        public static double LinearScale(double x) => math.pow(10, x / 10);
        public static float LinearScale(float x) => math.pow(10, x / 10);
        public static double LogScale(double x) => 10 * math.log10(x);
        public static float LogScale(float x) => 10 * math.log10(x);

        public static string PrettyPrintDataRate(double rate) => $"{PrettyPrint(rate)}bps";

        public static string PrettyPrint(double d)
        {
            if (d > 1e9) return $"{d / 1e9:F2} G";
            else if (d > 1e6) return $"{d / 1e6:F2} M";
            else if (d > 1e3) return $"{d / 1e3:F1} K";
            else return $"{d:F0} ";
        }

        public static string PrettyPrint(List<RealAntenna> list)
        {
            string s = string.Empty;
            foreach (RealAntenna ra in list)
            {
                s += $"{ra.RFBand.name}-Band: {ra.Gain:F1} dBi\n";
            }
            return s;
        }

        public static RealAntenna HighestGainCompatibleDSNAntenna(List<CommNode> nodes, RealAntenna peer)
        {
            RealAntenna result = null;
            double highestGain = 0;
            foreach (RACommNode node in nodes.Where(obj => obj.isHome))
            {
                foreach (RealAntenna ra in node.RAAntennaList)
                {
                    if (peer.Compatible(ra) && ra.Gain > highestGain)
                    {
                        highestGain = ra.Gain;
                        result = ra;
                    }
                }
            }
            return result;
        }

        public static string DisplayGamescenes(ProtoScenarioModule psm)
        {
            string s = string.Empty;
            foreach (GameScenes gs in psm.targetScenes) { s += $"{gs} "; }
            return $"{psm} {psm.moduleName} [{s}]";
        }

        public static string VesselWalk(RACommNetwork net, string ModTag = "[RealAntennas] ")
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{ModTag} VesselWalk()\n");
            sb.Append($"FlightData has {FlightGlobals.Vessels.Count} vessels.\n");
            foreach (Vessel v in FlightGlobals.Vessels.Where(x => x is Vessel && x.Connection == null))
            {
                sb.Append($"Vessel {v.vesselName} has a null connection.\n");
            }
            foreach (Vessel v in FlightGlobals.Vessels.Where(x => x is Vessel && x.Connection is CommNetVessel))
            {
                sb.Append($"Vessel {v.vesselName}: {v.Connection?.name} CommNode: {v.Connection?.Comm}\n");
                foreach (ModuleRealAntenna ra in v.FindPartModulesImplementing<ModuleRealAntenna>())
                {
                    sb.Append($"... Contains RealAntenna part {ra.part} / {ra}.\n");
                }
            }
            return sb.ToStringAndRelease();
        }
    }
}
