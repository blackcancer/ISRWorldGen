using ISRWorldGen.Core.Geology.Landscapes;

internal static class MassifCompositionChecks
{
    internal static int Run()
    {
        int checks = 0;
        Check(RawMassifComposition.Compose(0, 0, 1, 1) == 0, "Texture alone created a mountain");
        Check(RawMassifComposition.Sample(73, 10, 20, 1, 0, 0, 0) == 0, "Empty geological support created uplift");
        Check(RawMassifComposition.Compose(.64, 0, .5, .5) > .20, "All uplift is confined to the axial skeleton");
        for (int i = 0; i <= 100; i++)
        {
            double support = i / 100d, texture = ((i * 37) % 101) / 100d, fine = ((i * 53) % 101) / 100d;
            double withoutCrest = RawMassifComposition.Compose(support, 0, texture, fine);
            double withCrest = RawMassifComposition.Compose(support, 1, texture, fine);
            Check(withoutCrest >= 0 && withCrest <= .766000000000001, "Analytic composition bound failed");
            Check(withCrest >= withoutCrest && withCrest - withoutCrest <= .220000000000001,
                "Axial skeleton dominates the entire relief again");
            if (i < 100)
                Check(RawMassifComposition.Compose((i + 1) / 100d, .3, texture, fine) >=
                    RawMassifComposition.Compose(support, .3, texture, fine), "Increasing uplift reduced the massif");
        }
        for (int i = 0; i < 128; i++)
        {
            double x = 71.25 * i - 1024, z = 317.5 * i + 181;
            double a = RawMassifComposition.Sample(73, x, z, 1, .7, .2, .3);
            double b = RawMassifComposition.Sample(73, x / 2, z / 2, .5, .7, .2, .3);
            Check(Math.Abs(a - b) < 1e-12, "Massif wavelength depends on raster pixels rather than physical scale");
            Check(a == RawMassifComposition.Sample(73, x, z, 1, .7, .2, .3), "Stateless repeated sample changed");
        }
        double[] serial = Enumerable.Range(0, 128).Select(i => RawMassifComposition.Sample(73, i * 751, i * 397, 1, .8, .1, .2)).ToArray();
        Parallel.For(0, serial.Length, i =>
        {
            if (serial[i] != RawMassifComposition.Sample(73, i * 751, i * 397, 1, .8, .1, .2))
                throw new InvalidOperationException("Concurrent massif sample changed");
        });
        Check(serial.Distinct().Count() > 100, "Declared province has no internal morphology");
        Check(RawMassifComposition.Sample(73, 1234, 5678, 1, .8, .1, .2) !=
            RawMassifComposition.Sample(74, 1234, 5678, 1, .8, .1, .2), "Massif morphology ignores seed");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -.1, 1.1 })
        {
            Refuse(() => RawMassifComposition.Compose(invalid, 0, 0, 0));
            Refuse(() => RawMassifComposition.Compose(0, invalid, 0, 0));
            Refuse(() => RawMassifComposition.Compose(0, 0, invalid, 0));
            Refuse(() => RawMassifComposition.Compose(0, 0, 0, invalid));
        }
        Refuse(() => RawMassifComposition.Sample(73, double.NaN, 0, 1, 0, 0, 0));
        Refuse(() => RawMassifComposition.Sample(73, 0, 0, 0, 0, 0, 0));
        Console.WriteLine($"MASSIF_COMPOSITION_CHECKS={checks}; structural invariants only; geographic acceptance remains separate");
        return checks;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        void Refuse(Action action)
        {
            try { action(); } catch (ArgumentException) { checks++; return; }
            throw new InvalidOperationException("Invalid composition input was accepted");
        }
    }
}
