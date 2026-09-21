using ISRWorldGen.Core.Geology.Landscapes;
using static ISRWorldGen.Core.Geology.Landscapes.RawReliefStructure;

// In-process regression checks of the actual production helper through the
// narrowly scoped friend assembly. No copied relief implementation is used.
internal static class RawReliefChecks
{
    internal static int Run()
    {
        int checks = 0;
        Segment[] whole = [new(0, 0, 120, 0)];
        Segment[] split = [new(0, 0, 40, 0), new(40, 0, 75, 0), new(75, 0, 120, 0)];
        Check(double.IsPositiveInfinity(DistanceSquared(0, 0, [])), "Empty contact has no influence");
        Check(DistanceSquared(60, 7, whole) == 49, "Normal distance incorrect");
        Check(DistanceSquared(123, 4, whole) == 25, "End cap distance incorrect");
        Check(DistanceSquared(-3, -4, whole) == 25, "Start cap distance incorrect");
        Check(DistanceSquared(20, 0, whole) == 0, "On-contact distance incorrect");
        for (int i = -20; i <= 140; i++)
        {
            double z = i % 17 - 8;
            double expected = DistanceSquared(i, z, whole);
            Check(Math.Abs(expected - DistanceSquared(i, z, split)) < 1e-10, "Subdivision changed contact distance");
            Check(DistanceSquared(i, z, split) == DistanceSquared(i, z, split.Reverse().ToArray()), "Enumeration changed contact distance");
            Check(DistanceSquared(i, z, split) == DistanceSquared(i, z, split.Concat(split).ToArray()), "Duplicate segment added influence");
        }
        bool refused = false;
        try { DistanceSquared(0, 0, [new(1, 1, 1, 1)]); }
        catch (ArgumentException) { refused = true; }
        Check(refused, "Degenerate contact accepted");
        Check(Fade(0) == 0 && Fade(1) == 1, "Blend endpoints changed");
        Check(Smooth(-3, 5, -10) == 0 && Smooth(-3, 5, 10) == 1, "Support outside bounds");
        for (int i = 0; i < 256; i++)
        {
            double x = (i - 128) * 73.25, z = (i % 41 - 20) * 111.5;
            double f = Fractal(73, x, z, 6500, 12), m = Mountain(73, x, z, 4200, 41);
            Check(double.IsFinite(f) && f >= -1 && f <= 1, "Fractal envelope exceeded");
            Check(double.IsFinite(m) && m >= 0 && m <= 1, "Supported mountain envelope exceeded");
            Check(m == Mountain(73, x, z, 4200, 41), "Stateless detail changed");
        }
        Check(Mountain(73, 1771, 3677, 4200, 41) != Mountain(74, 1771, 3677, 4200, 41), "Seed-blind detail");
        Console.WriteLine($"RAW_STRUCTURE_CHECKS={checks}; geometry only, not geographic acceptance");
        return checks;
        void Check(bool ok, string why)
        { if (!ok) throw new InvalidOperationException(why); checks++; }
    }
}
