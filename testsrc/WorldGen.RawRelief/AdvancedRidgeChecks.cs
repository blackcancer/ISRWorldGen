using ISRWorldGen.Core.Geology.Landscapes;
using static ISRWorldGen.Core.Geology.Landscapes.RawReliefStructure;

internal static class AdvancedRidgeChecks
{
    internal static int Run()
    {
        int checks = 0;
        Segment[] geometry = [new(0, 0, 40000, 0)];
        var axis = new RawRidgeNetwork.Source(geometry, 2400, .8);
        var first = new RawRidgeNetwork(73, [axis], 1);
        var divided = new RawRidgeNetwork(73, [axis with { Segments = [new(15000, 0, 0, 0), new(40000, 0, 30000, 0), new(15000, 0, 30000, 0)] }], 1);
        Check(first.SegmentCount == divided.SegmentCount, "Canonical planning count changed under subdivision");
        for (int i = 0; i < 700; i++)
        {
            double x = i * 67 - 3000, z = (i * 181 % 16000) - 8000;
            Check(first.Sample(x, z) == divided.Sample(x, z), "Canonical field changed under subdivision/reversal");
        }
        double middle = first.Sample(20000, 0);
        Check(middle > 0 && first.Sample(0, 0) < middle * .5 && first.Sample(40000, 0) < middle * .5,
            "A mountain range retains abrupt high terminal caps");
        geometry[0] = new(0, 100000, 40000, 100000);
        Check(middle == first.Sample(20000, 0), "Caller modified immutable relief");
        Check(first.Sample(-100000, 100000) == 0, "Compact ridge footprint leaked");
        var serial = Enumerable.Range(0, 512).Select(i => first.Sample(i * 71, (i * 431 % 14000) - 7000)).ToArray();
        Parallel.For(0, serial.Length, i =>
        {
            if (serial[i] != first.Sample(i * 71, (i * 431 % 14000) - 7000)) throw new InvalidOperationException("Concurrent ridge sample changed");
        });
        Check(serial.All(v => double.IsFinite(v) && v >= 0 && v <= .8), "Invalid range height");
        var empty = new RawRidgeNetwork(73, [], 1);
        Check(empty.SegmentCount == 0 && empty.Sample(0, 0) == 0, "Unexplained uplift from empty geometry");
        var zero = new RawRidgeNetwork(73, [axis with { Strength = 0 }], 1);
        Check(zero.SegmentCount == 0, "Inactive uplift allocated ridges");
        Refuse(() => new RawRidgeNetwork(73, [axis], double.Epsilon));
        Refuse(() => new RawRidgeNetwork(73, [axis with { Width = double.MaxValue }], 1));
        Refuse(() => new RawRidgeNetwork(73, [axis with { Segments = [new(-1e12, 0, 1e12, 0)] }], 1));
        Refuse(() => new RawRidgeNetwork(73, [axis with { Segments = [new(0, 0, 0, 0)] }], 1));
        Refuse(() => first.Sample(double.NaN, 0));
        for (int i = -3; i < 12; i++) for (int j = -2; j <= 2; j++)
            Check(Math.Abs(first.Sample(i * 4096 - 1e-5, j * 711) - first.Sample(i * 4096 + 1e-5, j * 711)) < 1e-5,
                "Spatial bin boundary introduced a seam");
        Console.WriteLine($"ADVANCED_RIDGE_CHECKS={checks}; numerical only; erosion NOT_RUN");
        return checks;
        void Check(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); checks++; }
        void Refuse(Action action)
        {
            try { action(); } catch (ArgumentException) { checks++; return; }
            throw new InvalidOperationException("Unsafe ridge input was accepted");
        }
    }
}
