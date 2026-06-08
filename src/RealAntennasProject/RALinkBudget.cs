namespace RealAntennas
{
    public sealed class RALinkBudget
    {
        public RACommNode txNode;
        public RACommNode rxNode;
        public RealAntenna tx;
        public RealAntenna rx;
        public double dataRate;
        public double maxDataRate;
        public double metric;
    }
}
