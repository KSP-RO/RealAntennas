using SaveUpgradePipeline;
using System;

namespace RealAntennas.UpgradeScripts
{
    [UpgradeModule(LoadContext.SFS | LoadContext.Craft, sfsNodeUrl = "GAME/FLIGHTSTATE/VESSEL/PART", craftNodeUrl = "PART")]
    public class v2_6_AntennaState : UpgradeScript
    {
        public override string Name => "RealAntennas Antenna Condition upgrader";
        public override string Description => "Update crafts to use new condition enum";
        public override Version EarliestCompatibleVersion => new Version(1, 0, 0);
        public override Version TargetVersion => new Version(2, 6, 0);

        public override TestResult OnTest(ConfigNode node, LoadContext loadContext, ref string nodeName)
        {
            nodeName = NodeUtil.GetPartNodeNameValue(node, loadContext);
            if (node.GetNode("MODULE", "name", nameof(ModuleRealAntenna)) is ConfigNode mecNode)
                return TestResult.Upgradeable;
            return TestResult.Pass;
        }

        public override void OnUpgrade(ConfigNode node, LoadContext loadContext, ConfigNode parentNode)
        {
            var pmNode = node.GetNode("MODULE", "name", nameof(ModuleRealAntenna));
            var v = pmNode.GetValue("_enabled");
            var newState = string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) ? AntennaCondition.Disabled : AntennaCondition.Enabled;
            pmNode.AddValue(nameof(ModuleRealAntenna.Condition), newState.ToString());
            pmNode.RemoveValue("_enabled");
        }
    }

    public class v2_6_AntennaState_KCTBase : v2_6_AntennaState
    {
        public override string Name { get => $"{base.Name} KCT-{nodeUrlSFS}"; }
        public override TestResult OnTest(ConfigNode node, LoadContext loadContext, ref string nodeName) =>
            loadContext == LoadContext.Craft ? TestResult.Pass : base.OnTest(node, loadContext, ref nodeName);
    }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/VABList/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT1 : v2_6_AntennaState_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/SPHList/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT2 : v2_6_AntennaState_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/VABWarehouse/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT3 : v2_6_AntennaState_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/SPHWarehouse/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT4 : v2_6_AntennaState_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/VABPlans/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT5 : v2_6_AntennaState_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/SPHPlans/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT6 : v2_6_AntennaState_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/LaunchComplex/BuildList/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT7 : v2_6_AntennaState_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/LaunchComplex/Warehouse/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT8 : v2_6_AntennaState_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/LaunchComplex/Plans/KCTVessel/ShipNode/PART")]
    public class v2_6_AntennaState_KCT9 : v2_6_AntennaState_KCTBase { }
}
