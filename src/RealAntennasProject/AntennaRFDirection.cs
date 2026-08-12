using System;

namespace RealAntennas
{
    
    // The physical RF direction(s) a transceiver is capable of. Named in
    // hardware terms (Transmit/Receive) since "uplink"/"downlink" depends on reference
    [Flags]
    public enum AntennaRFDirection
    {
        Transmit = 1 << 0,
        Receive = 1 << 1,
        Bidirectional = Transmit | Receive
    }

    public static class AntennaRFDirectionExtensions
    {
        public const string DefaultModeName = "Bidirectional";
        // Order matters: this is also the UI_ChooseOption cycle order in the VAB.
        public static readonly string[] ModeNames = { "Uplink", "Downlink", DefaultModeName };
        public static readonly string[] ModeDisplayNames = { "Uplink Only (Receive)", "Downlink Only (Transmit)", "Bidirectional" };

        public static AntennaRFDirection FromModeName(string name) => name switch
        {
            "Uplink" => AntennaRFDirection.Receive,
            "Downlink" => AntennaRFDirection.Transmit,
            _ => AntennaRFDirection.Bidirectional,
        };

        public static bool CanTransmitRF(this AntennaRFDirection dir) => (dir & AntennaRFDirection.Transmit) != 0;
        public static bool CanReceiveRF(this AntennaRFDirection dir) => (dir & AntennaRFDirection.Receive) != 0;
    }
}
