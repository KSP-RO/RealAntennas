using System;

namespace RealAntennas
{
    // What kind of traffic an antenna/link is permitted to carry.
    // Telemetry for command & Control and Science for Science data
    [Flags]
    public enum AntennaDuty
    {
        Telemetry = 1 << 0,
        Science = 1 << 1,
        Both = Telemetry | Science
    }

    public static class AntennaDutyExtensions
    {
        public const string DefaultModeName = "Both";
        public static readonly string[] ModeNames = { "Telemetry", "Science", DefaultModeName };
        public static readonly string[] ModeDisplayNames = { "Telemetry Only", "Science Only", "Telemetry + Science" };

        public static AntennaDuty FromModeName(string name) => name switch
        {
            "Telemetry" => AntennaDuty.Telemetry,
            "Science" => AntennaDuty.Science,
            _ => AntennaDuty.Both,
        };

        public static string ToModeName(this AntennaDuty duty) => duty switch
        {
            AntennaDuty.Telemetry => "Telemetry",
            AntennaDuty.Science => "Science",
            _ => "Both",
        };

        public static bool CanHandleTelemetry(this AntennaDuty duty) => (duty & AntennaDuty.Telemetry) != 0;
        public static bool CanHandleScience(this AntennaDuty duty) => (duty & AntennaDuty.Science) != 0;
    }
}
