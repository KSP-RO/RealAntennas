using System;
using System.Collections.Generic;
using System.Reflection;

namespace RealAntennas.Kerbalism
{
    public class Kerbalism
    {
        public const string ModTag = "[RAKerbalismLink] ";
        private static bool detectionDone = false;
        public static Assembly KerbalismAssembly = null;
        private static FieldInfo fPowered, fTransmitting, fLinked, fEc, fRate, fStatus, fStrength, fTargetName, fControlPath, fEcIdle;

        private static void CacheFields(Type t)
        {
            fPowered = t.GetField("powered");
            fTransmitting = t.GetField("transmitting");
            fLinked = t.GetField("linked");
            fEc = t.GetField("ec");
            fRate = t.GetField("rate");
            fStatus = t.GetField("status");
            fStrength = t.GetField("strength");
            fTargetName = t.GetField("target_name");
            fControlPath = t.GetField("control_path");
            fEcIdle = t.GetField("ec_idle");
        }

        public static void RAKerbalismLinkHandler(object p1, Vessel v)
        {
            if (CommNet.CommNetScenario.CommNetEnabled && v.Connection is RACommNetVessel raCNV && raCNV.Comm is RACommNode node)
            {
                bool powered = (bool)fPowered.GetValue(p1);
                bool transmitting = (bool)fTransmitting.GetValue(p1);
                double ecIdle = powered ? raCNV.IdlePowerDraw() : 0;
                double ec = ecIdle, rate = 0, strength = 0, packetInterval = 1.0f;
                int status = 2;
                string target_name = "NotConnected";
                List<string[]> sList = new List<string[]>();
                if (!v.loaded) raCNV.powered = powered;
                if (powered && node.AntennaTowardsHome() is RealAntenna ra)
                {
                    CommNet.CommPath path = new CommNet.CommPath();
                    (node.Net as RACommNetwork).FindHome(node, path);
                    status = !raCNV.IsConnectedHome ? 2 : path.Count == 1 ? 0 : 1;
                    rate = (node.Net as RACommNetwork).MaxDataRateToHome(node) / 8e6;    // Convert rate from bps to MBps;
                    if (transmitting) ec += ra.PowerDrawLinear * packetInterval * 1e-6;    // 1 EC/sec = 1KW.  Draw(mw) * interval(sec) * mW -> kW conversion
                    if (node[path.First.end] is RACommLink link)
                    {
                        strength = link.start.Equals(node) ? link.FwdMetric : link.RevMetric;
                    }
                    foreach (CommNet.CommLink clink in path)
                    {
                        sList.Add(new string[1] { clink.end.displayName });
                    }
                    //sList.Add(new string[1] { path.First.end.name });
                    target_name = path.First.end.displayName;
                }

                fLinked.SetValue(p1, raCNV.IsConnectedHome);    // Link Status
                fEc.SetValue(p1, ec);                           // EC/s
                fRate.SetValue(p1, rate);                       // Rate in MB/s
                fStatus.SetValue(p1, status);                   // 0=direct, 1=indirect, 2=none
                fStrength.SetValue(p1, strength);               // Signal quality indicator (float 0..1)
                fTargetName.SetValue(p1, target_name);
                fControlPath.SetValue(p1, sList);
                fEcIdle?.SetValue(p1, ecIdle);

                //Debug.LogFormat($"{ModTag}Rate: {RATools.PrettyPrintDataRate(rate * 8 * 1024 * 1024)} EC: {ec:F4}  Linked:{raCNV.IsConnectedHome}  Strength: {strength:F2}  Target: {target_name}");
            }
        }

        public static bool DetectKerbalismDLL()
        {
            if (detectionDone) return KerbalismAssembly != null;
            detectionDone = true;

            foreach (var a in AssemblyLoader.loadedAssemblies)
            {
                if (a.name.StartsWith("Kerbalism") && 
                    !a.name.StartsWith("KerbalismBoot") &&
                    a.assembly.GetType("KERBALISM.API") is Type KerbalismAPIType
                    )
                {
                    KerbalismAssembly = a.assembly;
                    CacheFields(a.assembly.GetType("KERBALISM.ConnectionInfo", true));

                    // Kerbalism's API takes a MethodInfo and calls it via MethodInfo.Invoke
                    MethodInfo handler = typeof(Kerbalism).GetMethod(nameof(RAKerbalismLinkHandler));
                    object commInfo = KerbalismAPIType.GetField("Comm", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                    MethodInfo addMethod = commInfo.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
                    addMethod.Invoke(commInfo, new object[] { handler });
                    return true;
                }
            }
            return false;
        }
    }
}
