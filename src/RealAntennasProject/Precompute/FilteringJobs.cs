using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace RealAntennas.Precompute
{
    [BurstCompile]
    public struct FilterCommNodes : IJobParallelFor
    {
        [ReadOnly] public NativeArray<CNInfo> nodes;
        [ReadOnly] public NativeArray<int2> pairs;
        [WriteOnly] public NativeArray<bool> valid;

        public void Execute(int index)
        {
            int x = pairs[index].x;
            int y = pairs[index].y;
            CNInfo a = nodes[x];
            CNInfo b = nodes[y];
            valid[index] = x != y && !(a.isGroundStation && b.isGroundStation) && a.canComm && b.canComm;
        }
    }

    [BurstCompile]
    public struct FilterCommNodesByOcclusion : IJobParallelFor
    {
        [ReadOnly] public NativeArray<CNInfo> nodes;
        [ReadOnly] public NativeArray<int2> pairs;
        [ReadOnly] public NativeArray<OccluderInfo> occluders;
        [ReadOnly] public NativeArray<bool> validIn;
        [WriteOnly] public NativeArray<bool> validOut;

        private bool Occluded(double3 a, double3 b, NativeArray<OccluderInfo> occluders)
        {
            // Given a, b, and a point v, the perpendicular distance from v to ab is:
            //  mag(av) if av dot ab < 0
            //  mag(bv) if bv dot ab > 0
            //  mag(ab cross av) / mag(ab) otherwise
            if (math.distancesq(a, b) < 1) return false;
            double3 ab = b - a;
            bool occluded = false;
            int i = 0;
            while (!occluded && i < occluders.Length)
            {
                double3 v = occluders[i].position;
                double3 av = v - a;
                double3 bv = v - b;
                double dist;
                if (math.dot(av, ab) < 0)
                    dist = math.length(av);
                else if (math.dot(bv, ab) > 0)
                    dist = math.length(bv);
                else
                    dist = math.length(math.cross(ab, av)) / math.length(ab);
                occluded |= dist <= occluders[i].radius;
                i++;
            }
            return occluded;
        }

        public void Execute(int index)
        {
            int x = pairs[index].x;
            int y = pairs[index].y;
            validOut[index] = validIn[index] && !Occluded(nodes[x].position, nodes[y].position, occluders);
        }
    }

    // valid is indexed by x * nodeCount + y, matching the layout of the node pair list.
    [BurstCompile]
    public struct FilterAntennaPairsJob : IJob
    {
        [ReadOnly] public NativeArray<bool> valid;
        [ReadOnly] public NativeArray<int4> allPairs;
        public int nodeCount;
        [WriteOnly] public NativeList<int4> validPairs;

        public void Execute()
        {
            for (int i = 0; i < allPairs.Length; i++)
            {
                int4 p = allPairs[i];
                if (valid[p.x * nodeCount + p.y])
                    validPairs.Add(p);
            }
        }
    }
}
