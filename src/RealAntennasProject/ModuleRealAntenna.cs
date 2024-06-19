using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RealAntennas
{
    public class ModuleRealAntenna : ModuleDataTransmitter, IPartCostModifier, IPartMassModifier
    {
        private const string PAWGroup = "RealAntennas";
        private const string PAWGroupPlanner = "Antenna Planning";
        [KSPField(guiActiveEditor = true, guiName = "Antenna", isPersistant = true, groupName = PAWGroup, groupDisplayName = PAWGroup),
        UI_Toggle(disabledText = "<color=red><b>Disabled</b></color>", enabledText = "<color=green>Enabled</color>", scene = UI_Scene.Editor)]
        public bool _enabled = true;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Gain", guiUnits = " dBi", guiFormat = "F1", groupName = PAWGroup, groupDisplayName = PAWGroup)]
        public float Gain;          // Physical directionality, measured in dBi

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Transmit Power (dBm)", guiUnits = " dBm", guiFormat = "F1", groupName = PAWGroup),
        UI_FloatRange(maxValue = 60, minValue = 0, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float TxPower = 30;       // Transmit Power in dBm (milliwatts)

        [KSPField] protected float MaxTxPower = 60;    // Per-part max setting for TxPower

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Tech Level", guiFormat = "N0", groupName = PAWGroup),
        UI_FloatRange(minValue = 0f, stepIncrement = 1f, scene = UI_Scene.Editor)]
        private float TechLevel = -1f;
        private int techLevel => Convert.ToInt32(TechLevel);

        [KSPField] private int maxTechLevel = 0;
        [KSPField(isPersistant = true)] public float AMWTemp;    // Antenna Microwave Temperature
        [KSPField(isPersistant = true)] public float antennaDiameter = 0;
        [KSPField(isPersistant = true)] public float referenceGain = 0;
        [KSPField(isPersistant = true)] public float referenceFrequency = 0;
        [KSPField] public bool applyMassModifier = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "RF Band", groupName = PAWGroup),
         UI_ChooseOption(scene = UI_Scene.Editor)]
        public string RFBand = "S";

        public Antenna.BandInfo RFBandInfo => Antenna.BandInfo.All[RFBand];

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Power (Active)", groupName = PAWGroup)]
        public string sActivePowerConsumed = string.Empty;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Power (Idle)", groupName = PAWGroup)]
        public string sIdlePowerConsumed = string.Empty;

        [KSPField(guiActive = true, guiName = "Antenna Target", groupName = PAWGroup)]
        public string sAntennaTarget = string.Empty;

        public Targeting.AntennaTarget Target { get => RAAntenna.Target; set => RAAntenna.Target = value; }

        [KSPField(guiName = "Active Transmission Time", guiFormat = "P0", groupName = PAWGroup),
         UI_FloatRange(minValue = 0, maxValue = 1, stepIncrement = 0.01f, scene = UI_Scene.Editor)]
        public float plannerActiveTxTime = 0;

        protected const string ModTag = "[ModuleRealAntenna] ";
        public static readonly string ModuleName = "ModuleRealAntenna";
        public RealAntenna RAAntenna;
        public PlannerGUI plannerGUI;

        private ModuleDeployableAntenna deployableAntenna;
        public bool Deployable => deployableAntenna != null;
        public bool Deployed => deployableAntenna?.deployState == ModuleDeployablePart.DeployState.EXTENDED;
        public float ElectronicsMass(TechLevelInfo techLevel, float txPower) => (techLevel.BaseMass + techLevel.MassPerWatt * txPower) / 1000;

        private float StockRateModifier = 0.001f;
        public static float InactivePowerConsumptionMult = 0.1f;
        private float DefaultPacketInterval = 1.0f;
        private bool scienceMonitorActive = false;
        private int actualMaxTechLevel = 0;

        public float PowerDraw => RATools.LogScale(PowerDrawLinear);
        public float PowerDrawLinear => RATools.LinearScale(TxPower) / RAAntenna.PowerEfficiency;

        [KSPEvent(active = true, guiActive = true, guiName = "Antenna Targeting", groupName = PAWGroup)]
        void AntennaTargetGUI() => Targeting.AntennaTargetManager.AcquireGUI(RAAntenna);

        [KSPEvent(active = true, guiActive = true, guiActiveEditor = true, guiName = "Antenna Planning", groupName = PAWGroup)]
        public void AntennaPlanningGUI()
        {
            plannerGUI = new GameObject($"{RAAntenna.Name}-Planning").AddComponent<PlannerGUI>();
            plannerGUI.primaryAntenna = RAAntenna;
            var homes = RACommNetScenario.GroundStations.Values.Where(x => x.Comm is RACommNode);
            plannerGUI.fixedAntenna = plannerGUI.GetBestMatchingGroundStation(RAAntenna, homes) is RealAntenna bestDSNAntenna ? bestDSNAntenna : RAAntenna;
            plannerGUI.parentPartModule = this;
        }

        [KSPEvent(active = true, guiActive = true, name = "Debug Antenna", groupName = PAWGroup)]
        public void DebugAntenna()
        {
            var dbg = new GameObject($"Antenna Debugger: {part.partInfo.title}").AddComponent<Network.ConnectionDebugger>();
            dbg.antenna = RAAntenna;
        }

        public override void OnAwake()
        {
            base.OnAwake();
            RAAntenna = new RealAntennaDigital(part.partInfo?.title ?? part.name);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (node.name != "CURRENTUPGRADE")
                Configure(node);
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (RAAntenna.CanTarget)
                RAAntenna.Target?.Save(node);
        }

        public void OnDestroy()
        {
            GameEvents.OnGameSettingsApplied.Remove(ApplyGameSettings);
            GameEvents.OnPartUpgradePurchased.Remove(OnPartUpgradePurchased);
        }

        public void Configure(ConfigNode node)
        {
            RAAntenna.Name = part.partInfo?.title ?? part.name;
            RAAntenna.Parent = this;
            RAAntenna.LoadFromConfigNode(node);
            Gain = RAAntenna.Gain;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            SetupBaseFields();
            Fields[nameof(_enabled)].uiControlEditor.onFieldChanged = OnAntennaEnableChange;
            (Fields[nameof(TxPower)].uiControlEditor as UI_FloatRange).maxValue = MaxTxPower;

            actualMaxTechLevel = maxTechLevel;    // maxTechLevel value can come from applied PartUpgrades
            int maxLvlFromParams = HighLogic.CurrentGame.Parameters.CustomParams<RAParameters>().MaxTechLevel;
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
            {
                maxTechLevel = actualMaxTechLevel = maxLvlFromParams;
            }
            else if (RATools.RP1Found)
            {
                // With RP-1 present, always allow selecting all TLs but validate the user choice on vessel getting built
                maxTechLevel = maxLvlFromParams;
            }
            UpdateMaxTechLevelInUI();
            if (TechLevel < 0) TechLevel = actualMaxTechLevel;

            RAAntenna.Name = part.partInfo.title;
            if (!RAAntenna.CanTarget)
            {
                Fields[nameof(sAntennaTarget)].guiActive = false;
                Events[nameof(AntennaTargetGUI)].active = false;
            }

            deployableAntenna = part.FindModuleImplementing<ModuleDeployableAntenna>();

            ApplyGameSettings();
            SetupUICallbacks();
            ConfigBandOptions();
            SetupIdlePower();
            RecalculateFields();
            SetFieldVisibility(_enabled);
            ApplyTLColoring();

            if (HighLogic.LoadedSceneIsFlight)
            {
                isEnabled = _enabled;
                if (_enabled)
                    GameEvents.OnGameSettingsApplied.Add(ApplyGameSettings);
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.OnPartUpgradePurchased.Add(OnPartUpgradePurchased);
            }
        }

        private void SetupIdlePower()
        {
            if (HighLogic.LoadedSceneIsFlight && _enabled)
            {
                var electricCharge = resHandler.inputResources.First(x => x.id == PartResourceLibrary.ElectricityHashcode);
                electricCharge.rate = (Kerbalism.Kerbalism.KerbalismAssembly is null) ? RAAntenna.IdlePowerDraw : 0;
                string err = "";
                resHandler.UpdateModuleResourceInputs(ref err, 1, 1, false, false);
            }
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && _enabled)
            {
                RAAntenna.AMWTemp = (AMWTemp > 0) ? AMWTemp : Convert.ToSingle(part.temperature);
                //part.AddThermalFlux(req / Time.fixedDeltaTime);
                if (Kerbalism.Kerbalism.KerbalismAssembly is null)
                {
                    string err = "";
                    resHandler.UpdateModuleResourceInputs(ref err, 1, 1, true, false);
                }
            }
        }

        private void RecalculateFields()
        {
            RAAntenna.TechLevelInfo = TechLevelInfo.GetTechLevel(techLevel);
            RAAntenna.TxPower = TxPower;
            RAAntenna.RFBand = Antenna.BandInfo.All[RFBand];
            RAAntenna.SymbolRate = RAAntenna.RFBand.MaxSymbolRate(techLevel);
            RAAntenna.Gain = Gain = (antennaDiameter > 0) ? Physics.GainFromDishDiamater(antennaDiameter, RFBandInfo.Frequency, RAAntenna.AntennaEfficiency) : Physics.GainFromReference(referenceGain, referenceFrequency * 1e6f, RFBandInfo.Frequency);
            double idleDraw = RAAntenna.IdlePowerDraw * 1000;
            sIdlePowerConsumed = $"{idleDraw:F2} Watts";
            sActivePowerConsumed = $"{idleDraw + (PowerDrawLinear / 1000):F2} Watts";
            int ModulationBits = (RAAntenna as RealAntennaDigital).modulator.ModulationBitsFromTechLevel(TechLevel);
            (RAAntenna as RealAntennaDigital).modulator.ModulationBits = ModulationBits;

            RecalculatePlannerECConsumption();
            if (plannerGUI is PlannerGUI)
                plannerGUI.RequestUpdate = true;
        }

        private void SetupBaseFields()
        {
            { if (Events[nameof(TransmitIncompleteToggle)] is BaseEvent be) be.active = false; }
            { if (Events[nameof(StartTransmission)] is BaseEvent be) be.active = false; }
            { if (Events[nameof(StopTransmission)] is BaseEvent be) be.active = false; }
            if (Actions[nameof(StartTransmissionAction)] is BaseAction ba) ba.active = false;
            if (Fields[nameof(powerText)] is BaseField bf) bf.guiActive = bf.guiActiveEditor = false;      // "Antenna Rating"
        }

        private void SetFieldVisibility(bool en)
        {
            Fields[nameof(Gain)].guiActiveEditor = Fields[nameof(Gain)].guiActive = en;
            Fields[nameof(TxPower)].guiActiveEditor = Fields[nameof(TxPower)].guiActive = en;
            Fields[nameof(TechLevel)].guiActiveEditor = Fields[nameof(TechLevel)].guiActive = en;
            Fields[nameof(RFBand)].guiActiveEditor = Fields[nameof(RFBand)].guiActive = en;
            Fields[nameof(sActivePowerConsumed)].guiActiveEditor = Fields[nameof(sActivePowerConsumed)].guiActive = en;
            Fields[nameof(sIdlePowerConsumed)].guiActiveEditor = Fields[nameof(sIdlePowerConsumed)].guiActive = en;
            Fields[nameof(sAntennaTarget)].guiActive = en;
            Fields[nameof(plannerActiveTxTime)].guiActiveEditor = Kerbalism.Kerbalism.KerbalismAssembly is System.Reflection.Assembly;
        }

        private void SetupUICallbacks()
        {
            Fields[nameof(TechLevel)].uiControlEditor.onFieldChanged = OnTechLevelChange;
            Fields[nameof(TechLevel)].uiControlEditor.onSymmetryFieldChanged = OnTechLevelChangeSymmetry;
            Fields[nameof(RFBand)].uiControlEditor.onFieldChanged = OnRFBandChange;
            Fields[nameof(TxPower)].uiControlEditor.onFieldChanged = OnTxPowerChange;
            Fields[nameof(plannerActiveTxTime)].uiControlEditor.onFieldChanged += OnPlannerActiveTxTimeChanged;
        }

        private void UpdateMaxTechLevelInUI()
        {
            if (Fields[nameof(TechLevel)].uiControlEditor is UI_FloatRange fr)
            {
                fr.maxValue = maxTechLevel;
                if (fr.maxValue == fr.minValue)
                    fr.maxValue += 0.001f;
            }
        }

        private void OnPlannerActiveTxTimeChanged(BaseField field, object obj) => RecalculatePlannerECConsumption();
        private void OnAntennaEnableChange(BaseField field, object obj) { SetFieldVisibility(_enabled); RecalculatePlannerECConsumption(); }
        private void OnRFBandChange(BaseField f, object obj) => RecalculateFields();
        private void OnTxPowerChange(BaseField f, object obj) => RecalculateFields();
        private void OnTechLevelChange(BaseField f, object obj)     // obj is the OLD value
        {
            ApplyTLColoring();
            string oldBand = RFBand;
            ConfigBandOptions();
            RecalculateFields();
            if (!oldBand.Equals(RFBand)) MonoUtilities.RefreshPartContextWindow(part);
        }

        private void OnTechLevelChangeSymmetry(BaseField f, object obj) => ConfigBandOptions();

        private void ApplyGameSettings()
        {
            StockRateModifier = HighLogic.CurrentGame.Parameters.CustomParams<RAParameters>().StockRateModifier;
        }

        /// <summary>
        /// Handles TL PartUpgrade getting purchased in Editor scene
        /// </summary>
        /// <param name="upgd"></param>
        private void OnPartUpgradePurchased(PartUpgradeHandler.Upgrade upgd)
        {
            var tlInf = TechLevelInfo.GetTechLevel(upgd.name);
            if (tlInf != null && tlInf.Level > actualMaxTechLevel)
            {
                actualMaxTechLevel = tlInf.Level;
                if (!RATools.RP1Found) maxTechLevel = actualMaxTechLevel;
                UpdateMaxTechLevelInUI();
                ApplyTLColoring();
            }
        }

        private void ApplyTLColoring()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                BaseField f = Fields[nameof(TechLevel)];
                f.guiFormat = techLevel > actualMaxTechLevel ? "'<color=orange>'#'</color>'" : "N0";
                f.guiName = techLevel > actualMaxTechLevel ? "<color=orange>Tech Level</color>" : "Tech Level";
            }
        }

        private void ConfigBandOptions()
        {
            List<string> availableBands = new List<string>();
            List<string> availableBandDisplayNames = new List<string>();
            foreach (Antenna.BandInfo bi in Antenna.BandInfo.GetFromTechLevel(techLevel))
            {
                availableBands.Add(bi.name);
                availableBandDisplayNames.Add($"{bi.name}-Band");
            }

            UI_ChooseOption op = (UI_ChooseOption)Fields[nameof(RFBand)].uiControlEditor;
            op.options = availableBands.ToArray();
            op.display = availableBandDisplayNames.ToArray();
            if (op.options.IndexOf(RFBand) < 0)
                RFBand = op.options[op.options.Length - 1];
        }

        public override string GetModuleDisplayName() => "RealAntenna";
        public override string GetInfo()
        {
            string res = string.Empty;
            if (RAAntenna.Shape != AntennaShape.Omni)
            {
                foreach (Antenna.BandInfo band in Antenna.BandInfo.All.Values)
                {
                    float tGain = (antennaDiameter > 0) ? Physics.GainFromDishDiamater(antennaDiameter, band.Frequency, RAAntenna.AntennaEfficiency) : Physics.GainFromReference(referenceGain, referenceFrequency * 1e6f, band.Frequency);
                    res += $"<color=green><b>{band.name}</b></color>: {tGain:F1} dBi, {Physics.Beamwidth(tGain):F1} beamwidth\n";
                }
            } else
            {
                res = $"<color=green>Omni-directional</color>: {Gain:F1} dBi";
            }
            return res;
        }

        public override bool CanComm() => base.CanComm() && (!Deployable || Deployed);

        public override string ToString() => RAAntenna.ToString();

        #region Stock Science Transmission
        // StartTransmission -> CanTransmit()
        //                  -> OnStartTransmission() -> queueVesselData(), transmitQueuedData()
        // (Science) -> TransmitData() -> TransmitQueuedData()

        internal void SetTransmissionParams()
        {
            if (RACommNetScenario.CommNetEnabled && this?.vessel?.Connection?.Comm is RACommNode node)
            {
                double data_rate = (node.Net as RACommNetwork).MaxDataRateToHome(node);
                packetInterval = DefaultPacketInterval;
                packetSize = Convert.ToSingle(data_rate * packetInterval);
                packetSize *= StockRateModifier;
                packetResourceCost = PowerDrawLinear * packetInterval * 1e-6; // 1 EC/sec = 1KW.  Draw(mw) * interval(sec) * mW->kW conversion
                Debug.Log($"{ModTag} Setting transmission params: rate: {data_rate:F1}, interval: {packetInterval:F1}s, rescale: {StockRateModifier:N5}, size: {packetSize:N6}");
            }
        }
        public override bool CanTransmit()
        {
            SetTransmissionParams();
            return base.CanTransmit();
        }

        public override void TransmitData(List<ScienceData> dataQueue)
        {
            SetTransmissionParams();
            base.TransmitData(dataQueue);
            if (!scienceMonitorActive)
                StartCoroutine(StockScienceFixer());
        }

        public override void TransmitData(List<ScienceData> dataQueue, Callback callback)
        {
            SetTransmissionParams();
            base.TransmitData(dataQueue, callback);
            if (!scienceMonitorActive)
                StartCoroutine(StockScienceFixer());
        }

        private IEnumerator StockScienceFixer()
        {
            System.Reflection.BindingFlags flag = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            float threshold = 0.999f;
            scienceMonitorActive = true;
            while (busy || transmissionQueue.Count > 0)
            {
                if (commStream is RnDCommsStream)
                {
                    float dataIn = (float)commStream.GetType().GetField("dataIn", flag).GetValue(commStream);
                    //Debug.Log($"{ModTag} StockScienceFixer: Current: {dataIn} / {commStream.fileSize}, delivered: {packetSize}");
                    if (dataIn == commStream.fileSize)
                    {
                        Debug.Log($"{ModTag} Stock Science Transfer delivered {dataIn} Mits successfully");
                        yield return new WaitForSeconds(packetInterval * 2);
                    }
                    else if (dataIn / commStream.fileSize >= threshold)
                    {
                        Debug.Log($"{ModTag} StockScienceFixer stuffing the last segment of data...");
                        commStream.StreamData(commStream.fileSize * 0.1f, vessel.protoVessel);
                        yield return new WaitForSeconds(packetInterval * 2);
                    }
                }
                yield return new WaitForSeconds(packetInterval);
            }
            scienceMonitorActive = false;
            Debug.Log($"{ModTag} StockScienceFixer: transmissions complete");
        }

        #endregion

        #region Cost and Mass Modifiers
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) =>
            _enabled ? RAAntenna.TechLevelInfo.BaseCost + (RAAntenna.TechLevelInfo.CostPerWatt * RATools.LinearScale(TxPower)/1000) : 0;
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) =>
            _enabled && applyMassModifier ? (RAAntenna.TechLevelInfo.BaseMass + (RAAntenna.TechLevelInfo.MassPerWatt * RATools.LinearScale(TxPower) / 1000)) / 1000 : 0;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        #endregion

        private KeyValuePair<string, double> plannerECConsumption = new KeyValuePair<string, double>("ElectricCharge", 0);

        public string PlannerUpdate(List<KeyValuePair<string, double>> resources, CelestialBody _, Dictionary<string, double> environment)
        {
            resources.Add(plannerECConsumption);   // ecConsumption is updated by the Toggle event
            return "comms";
        }
        private void RecalculatePlannerECConsumption()
        {
            // RAAntenna.IdlePowerDraw is in kW (ec/s), PowerDrawLinear is in mW
            double ec = _enabled ? RAAntenna.IdlePowerDraw + (RAAntenna.PowerDrawLinear * 1e-6 * plannerActiveTxTime) : 0;
            plannerECConsumption = new KeyValuePair<string, double>("ElectricCharge", -ec);
        }

        #region RP-1 integration
        /// <summary>
        /// Called from RP-1 VesselBuildValidator
        /// </summary>
        /// <param name="validationError"></param>
        /// <param name="canBeResolved"></param>
        /// <param name="costToResolve"></param>
        /// <returns></returns>
        public virtual bool Validate(out string validationError, out bool canBeResolved, out float costToResolve, out string techToResolve)
        {
            validationError = null;
            canBeResolved = false;
            costToResolve = 0;
            techToResolve = string.Empty;

            if (!_enabled || techLevel <= actualMaxTechLevel) return true;

            PartUpgradeHandler.Upgrade upgd = GetUpgradeForTL(techLevel);
            if (PartUpgradeManager.Handler.IsAvailableToUnlock(upgd.name))
            {
                canBeResolved = true;
                costToResolve = upgd.entryCost;
                validationError = $"purchase {upgd.title}";
            }
            else
            {
                techToResolve = upgd.techRequired;
                validationError = $"unlock tech {ResearchAndDevelopment.GetTechnologyTitle(upgd.techRequired)}";
            }

            return false;
        }

        /// <summary>
        /// Called from RP-1 VesselBuildValidator
        /// </summary>
        /// <returns></returns>
        public virtual bool ResolveValidationError()
        {
            PartUpgradeHandler.Upgrade upgd = GetUpgradeForTL(techLevel);
            if (upgd == null)
                return false;
            return PurchaseConfig(upgd);
        }

        private static bool PurchaseConfig(PartUpgradeHandler.Upgrade upgd)
        {
            CurrencyModifierQuery cmq = CurrencyModifierQuery.RunQuery(TransactionReasons.RnDPartPurchase, -upgd.entryCost, 0, 0);
            if (!cmq.CanAfford())
                return false;
            PartUpgradeManager.Handler.SetUnlocked(upgd.name, true);
            GameEvents.OnPartUpgradePurchased.Fire(upgd);
            return true;
        }

        private static PartUpgradeHandler.Upgrade GetUpgradeForTL(int techLevel)
        {
            TechLevelInfo tlInf = TechLevelInfo.GetTechLevel(techLevel);
            return PartUpgradeManager.Handler.GetUpgrade(tlInf.name);
        }
        #endregion
    }
}