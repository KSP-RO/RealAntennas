using System.Collections.Generic;

namespace RealAntennas.Network
{
    public sealed class LinkMetricsCollector
    {
        public RealAntenna Antenna { get; }

        public Dictionary<RealAntenna, List<LinkDetails>> Items { get; } =
            new Dictionary<RealAntenna, List<LinkDetails>>();

        public LinkMetricsCollector(RealAntenna antenna) => Antenna = antenna;

        public void Clear() => Items.Clear();
    }
}
