using ISRWorldGen.Core.Geology.Evolution;

internal static class CoupledTransportChecks
{
    internal static string[] Run()
    {
        var passed = new List<string>();
        const int n = 64;
        double[] east = Enumerable.Repeat(1d, n * n).ToArray(), south = new double[n * n];
        double[] pulse = Enumerable.Range(0, n * n).Select(i => 2 + Math.Exp(-Math.Pow((i % n - 22d) / 5, 2))).ToArray();
        double[][] initial = Bundle(pulse), actual = initial.Select(q => (double[])q.Clone()).ToArray();
        double[] donor = (double[])initial[0].Clone();
        for (int step = 0; step < 100; step++)
        {
            actual = CoupledCrustTransport.Advect(actual, east, south, n, 1, 1, .2);
            donor = CrustTransport.Advect(donor, east, south, n, 1, 1, .2);
        }
        double lowError = 0, highError = 0;
        for (int i = 0; i < n * n; i++)
        {
            double expected = initial[0][i / n * n + (i % n + n - 20) % n];
            lowError += Math.Abs(donor[i] - expected); highError += Math.Abs(actual[0][i] - expected);
        }
        Case("coupled transport reduces translation smearing against donor-cell", highError < .60 * lowError);
        for (int f = 0; f < 4; f++) CrustTransport.RequireBalance(CrustTransport.Sum(initial[f]), CrustTransport.Sum(actual[f]), "translation field " + f);
        Case("all four extensive fields remain conserved", true);
        Case("uniform age and inherited fraction stay attached to transported ocean",
            Enumerable.Range(0, pulse.Length).All(i => Math.Abs(actual[2][i] - 50 * actual[1][i]) < 1e-10 && Math.Abs(actual[3][i] - .7 * actual[1][i]) < 1e-11));
        var square = Bundle(Enumerable.Range(0, n * n).Select(i => i % n is >= 20 and < 40 ? 1d : 0d).ToArray());
        for (int step = 0; step < 20; step++) square = CoupledCrustTransport.Advect(square, east, south, n, 1, 1, .2);
        Case("discontinuous translation has no negative or excessive mass", square[0].All(v => v >= 0 && v <= 3 + 1e-12));
        var still = CoupledCrustTransport.Advect(initial, new double[n * n], south, n, 1, 1, .2);
        Case("zero motion neither deforms material nor modifies inputs", Enumerable.Range(0, 4).All(f => initial[f].SequenceEqual(still[f])) && initial[0][22] == 9);
        var zero = CoupledCrustTransport.Advect(initial, east, south, n, 1, 1, 0);
        zero[0][0] = -1;
        Case("zero-time results are fresh arrays", initial[0][0] > 0);
        double[] vx = Enumerable.Range(0, n * n).Select(i => .4 * Math.Sin(i * .17)).ToArray();
        double[] vz = Enumerable.Range(0, n * n).Select(i => .3 * Math.Cos(i * .23)).ToArray();
        var original = CoupledCrustTransport.Advect(initial, vx, vz, n, 2, 3, .3);
        var swapped = CoupledCrustTransport.Advect(initial.Select(q => Transpose(q, n)).ToArray(), Transpose(vz, n), Transpose(vx, n), n, 3, 2, .3);
        Case("metric axis exchange preserves the unsplit operator", Enumerable.Range(0, 4).All(f => Enumerable.Range(0, n * n).All(i => Math.Abs(original[f][i] - swapped[f][i % n * n + i / n]) < 1e-10)));
        Refuse(() => CoupledCrustTransport.Advect(initial, east, south, n, 1, 1, .5));
        Refuse(() => CoupledCrustTransport.Advect(initial, east, south, n, double.NaN, 1, .1));
        Refuse(() => CoupledCrustTransport.Advect([new double[4]], new double[4], new double[4], 2, 1, 1, .1));
        var invalid = Bundle([1, 1, 1, 1]); invalid[3][0] = 2;
        Refuse(() => CoupledCrustTransport.Advect(invalid, new double[4], new double[4], 2, 1, 1, .1));
        Refuse(() => new TectonicEvolutionSettings(advectionScheme: (CrustAdvectionScheme)100));
        Case("invalid CFL, material bundle and method are refused", true);
        var settings = new TectonicEvolutionSettings(side: 64, duration: 8, advectionScheme: CrustAdvectionScheme.CoupledMusclV1);
        var history = TectonicHistory.Generate(73, new TectonicScalePlan(1_000_000, 1_000_000), settings);
        var small = TectonicHistory.Generate(73, new TectonicScalePlan(131072, 131072), settings);
        Case("coupled history is exactly invariant to full-world resizing", history.Checksum == small.Checksum);
        var before = TectonicHistory.Generate(73, new TectonicScalePlan(1_000_000, 1_000_000), new TectonicEvolutionSettings(side: 64, duration: 8));
        Case("scheme changes the transport, not the initial geology", history.Initial.Checksum == before.Initial.Checksum && history.Final.Checksum != before.Final.Checksum);
        Console.WriteLine($"ADVECTION_TRANSLATION_L1 donor={lowError / pulse.Length:R} coupled={highError / pulse.Length:R}; no geographic acceptance");
        return passed.ToArray();
        void Case(string name, bool valid) { if (!valid) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("CHECK PASS: " + name); }
    }
    private static double[][] Bundle(double[] q) => new[] { q.Select(v => 3 * v).ToArray(), (double[])q.Clone(), q.Select(v => 50 * v).ToArray(), q.Select(v => .7 * v).ToArray() };
    private static double[] Transpose(double[] a, int n) => Enumerable.Range(0, a.Length).Select(i => a[i % n * n + i / n]).ToArray();
    private static void Refuse(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Unsafe transport input accepted."); }
}
