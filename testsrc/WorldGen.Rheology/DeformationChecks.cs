using ISRWorldGen.Core.Geology.Evolution;

internal static class DeformationChecks
{
    internal static string[] Run()
    {
        var names = new List<string>(); const int n = 32; int count = n * n;
        double[] zero = new double[count], one = Enumerable.Repeat(1d, count).ToArray();
        double[] mu = Enumerable.Range(0, count).Select(i => .3 + (i % 17) / 5d).ToArray();
        Case("zero forcing cannot create motion or terrain", () =>
        {
            var r = ThinSheetDeformation.Solve(zero, zero, mu, n, 1, 2, 3);
            Require(r.East.All(v => v == 0) && r.South.All(v => v == 0) && r.Dissipation == 0, "spontaneous motion");
        });
        Case("heterogeneous viscosity preserves common translation", () =>
        {
            var r = ThinSheetDeformation.Solve(one, one.Select(v => -2 * v).ToArray(), mu, n, 1, 2, 3);
            for (int i = 0; i < count; i++) { Near(r.East[i], 1, 1e-12); Near(r.South[i], -2, 1e-12); }
        });
        Case("zero coupling gives the prescribed face velocities exactly", () =>
        {
            var f = Enumerable.Range(0, count).Select(i => Math.Sin(i)).ToArray();
            var r = ThinSheetDeformation.Solve(f, zero, mu, n, 1, 1, 0);
            Require(r.East.SequenceEqual(f), "zero coupling changed forcing");
        });
        foreach (bool longitudinal in new[] { false, true }) Case("discrete Fourier " + (longitudinal ? "longitudinal" : "shear") + " analytic response", () =>
        {
            double[] f = Enumerable.Range(0, count).Select(i => Math.Sin(6 * Math.PI * (longitudinal ? i % n : i / n) / n)).ToArray();
            var r = ThinSheetDeformation.Solve(f, zero, one, n, 1, 1, 2);
            double factor = 1 + (longitudinal ? 4 : 1) * 16 * Math.Pow(Math.Sin(3 * Math.PI / n), 2);
            for (int i = 0; i < count; i++) { Near(r.East[i], f[i] / factor, 1e-10); Near(r.South[i], 0, 1e-10); }
        });
        Case("continuum Fourier error converges under metric refinement", () =>
        {
            double previous = double.PositiveInfinity;
            foreach (int side in new[] { 16, 32, 64 })
            {
                double[] f = Enumerable.Range(0, side * side).Select(i => Math.Sin(2 * Math.PI * (i / side + .5) / side)).ToArray();
                var r = ThinSheetDeformation.Solve(f, new double[f.Length], Enumerable.Repeat(1d, f.Length).ToArray(), side, 1d / side, 1d / side, .1);
                double exact = 1 / (1 + Math.Pow(.2 * Math.PI, 2));
                double error = r.East.Select((v, i) => Math.Abs(v - f[i] * exact)).Max();
                Require(error < previous * .35, "nonconvergent refinement"); previous = error;
            }
        });
        var a = Enumerable.Range(0, count).Select(i => Math.Sin(i * .37)).ToArray();
        var b = Enumerable.Range(0, count).Select(i => Math.Cos(i * .11)).ToArray();
        var result = ThinSheetDeformation.Solve(a, b, mu, n, 1, 2, 2);
        Case("actual force residual is below the declared tolerance", () => Require(result.RelativeResidual <= 1e-12, "force residual"));
        Case("basal work equals nonnegative viscous dissipation", () =>
        { Require(result.Dissipation >= 0, "negative dissipation"); Near(result.Work, result.Dissipation, Math.Max(1, result.Dissipation) * 1e-10); });
        Case("internal stresses cannot inject mean translation", () => Require(result.MeanVelocityError < 1e-10, "net internal force"));
        Case("coordinate unit rescaling preserves the solution", () =>
        {
            var r = ThinSheetDeformation.Solve(a, b, mu, n, 1000, 2000, 2000);
            for (int i = 0; i < count; i++) { Near(r.East[i], result.East[i], 1e-11); Near(r.South[i], result.South[i], 1e-11); }
        });
        Case("axis exchange preserves the coupled strain tensor", () =>
        {
            double[] T(IReadOnlyList<double> f) => Enumerable.Range(0, count).Select(i => f[(i % n) * n + i / n]).ToArray();
            var r = ThinSheetDeformation.Solve(T(b), T(a), T(mu), n, 2, 1, 2);
            double[] te = T(result.South), ts = T(result.East);
            for (int i = 0; i < count; i++) { Near(r.East[i], te[i], 1e-10); Near(r.South[i], ts[i], 1e-10); }
        });
        Case("a weak band localizes shear under identical forcing", () =>
        {
            bool Weak(int i) => Math.Min(Math.Abs(i / n - n / 4), Math.Abs(i / n - 3 * n / 4)) < 3;
            double[] f = Enumerable.Range(0, count).Select(i => Math.Cos(2 * Math.PI * (i / n + .5) / n)).ToArray();
            var viscosity = Enumerable.Range(0, count).Select(i => Weak(i) ? .25 : 4d).ToArray();
            var r = ThinSheetDeformation.Solve(f, zero, viscosity, n, 1, 1, 2);
            double weak = r.EngineeringShear.Where((_, i) => Weak(i)).Average(v => v * v);
            double strong = r.EngineeringShear.Where((_, i) => !Weak(i)).Average(v => v * v);
            Require(weak > 4 * strong, "rheology did not affect deformation location");
        });
        Case("warm and cold starts reach the same mechanical state", () =>
        {
            var r = ThinSheetDeformation.Solve(a, b, mu, n, 1, 2, 2, warmStart: result);
            for (int i = 0; i < count; i++) Near(r.East[i], result.East[i], 1e-10);
        });
        Case("input arrays remain unchanged", () =>
        { for (int i = 0; i < count; i++) { Near(a[i], Math.Sin(i * .37), 0); Near(mu[i], .3 + (i % 17) / 5d, 0); } });
        Case("iteration exhaustion refuses rather than silently using preferred velocities", () =>
            Refuse(() => ThinSheetDeformation.Solve(a, b, mu, n, 1, 2, 2, maximumIterations: 1)));
        Case("invalid coefficients and nonfinite forcing are rejected", () =>
        {
            Refuse(() => ThinSheetDeformation.Solve(one, zero, zero, n, 1, 1, 2));
            Refuse(() => ThinSheetDeformation.Solve(one, zero, mu, n, 0, 1, 2));
            Refuse(() => ThinSheetDeformation.Solve(one, zero, mu, n, 1, 1, double.NaN));
            Refuse(() => ThinSheetDeformation.Solve([double.NaN], zero, mu, n, 1, 1, 2));
        });
        Case("the constitutive prior is continuous and ocean age belongs to ocean material", () =>
        {
            Require(MaterialRheology.RelativeViscosity(0, 7, 80) > MaterialRheology.RelativeViscosity(0, 7, 0), "thermal prior reversed");
            Near(MaterialRheology.RelativeViscosity(35, 0, 0), MaterialRheology.RelativeViscosity(35, 0, 200), 0);
            for (int i = 0; i <= 100; i++) { double v = MaterialRheology.RelativeViscosity(i, 7, 50); Require(v >= .25 && v <= 4, "unbounded viscosity"); }
        });
        Case("actual face CFL is checked independently of preferred plate speeds", () =>
        { Near(MaterialRheology.StableStep(one.Select(v => 10 * v).ToArray(), zero, n, 1, 1), .035, 1e-15); });
        Case("mechanical upsampling preserves pure common translation", () =>
        {
            var state = MaterialPlateCohorts.Create(n, 1, new int[count], Enumerable.Repeat(35d, count).ToArray(), zero, zero, zero);
            string hash = state.ComputeChecksum();
            var r = MaterialRheology.Solve(state, [new(0, 0, 0, 2, -3)], 1, 1, 2, new(16));
            for (int i = 0; i < count; i++) { Near(r.East[i], 2, 1e-12); Near(r.South[i], -3, 1e-12); Near(r.Divergence[i], 0, 1e-12); }
            Require(hash == state.ComputeChecksum(), "mechanics altered material without transport");
        });
        var scale = new TectonicScalePlan(1_000_000, 1_000_000);
        var settings = new TectonicEvolutionSettings(side: 32, duration: 2);
        var assemblage = ContinentalAssemblage.Generate(73, scale, 32);
        MaterialBoundHistory? hist = null;
        Case("integrated heterogeneous sheet transports the same initial materials conservatively", () =>
        {
            hist = MaterialBoundHistory.GenerateWithRheology(73, scale, settings, assemblage, new(16));
            Require(hist.MechanicalSolves.Count == 3, "mechanical solve schedule changed");
            Require(hist.Initial.ContinentalKm.SequenceEqual(assemblage.ContinentalKm), "initial material mismatch");
            for (int p = 0; p < hist.Plates.Count; p++) Near(hist.InitialContinentalByOrigin[p], hist.FinalContinentalByOrigin[p], 1e-8);
        });
        Case("rheology is explicit in identity and does not replace the baseline", () =>
        {
            var baseline = MaterialBoundHistory.GenerateWithAssemblage(73, scale, settings, assemblage);
            Require(baseline.Rheology is null && baseline.MechanicalSolves.Count == 0 && baseline.Checksum != hist!.Checksum, "implicit switch");
            Require(baseline.Initial.ContinentalKm.SequenceEqual(hist!.Initial.ContinentalKm), "comparison changed initial state");
        });
        Case("full-atlas resizing preserves solved material geography", () =>
        {
            foreach (long size in new long[] { 131072, 262144, 1_000_000 })
                Near(hist!.SampleElevationKm(new(size, size), .234 * size, .619 * size), hist!.SampleElevationKm(scale, 234000, 619000), 1e-10);
        });
        return names.ToArray();
        void Case(string name, Action action) { action(); names.Add(name); Console.WriteLine("CHECK PASS: " + name); }
    }
    private static void Near(double a, double b, double tolerance)
    { if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Abs(a-b) > tolerance) throw new InvalidOperationException($"Mismatch {a:R} != {b:R} (tol {tolerance:R})"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Refuse(Action action)
    { try { action(); } catch (ArgumentException) { return; } catch (ArithmeticException) { return; } throw new InvalidOperationException("Invalid candidate accepted"); }
}
