using ISRWorldGen.Core.Geology.Evolution;

internal static class MaterialCohortChecks
{
    public static string[] Run()
    {
        var passed = new List<string>();
        Case("rigid one-cell translation carries the origin with its material", () =>
        {
            var state = Slabs(8, false);
            var moved = state.Advect(Enumerable.Repeat(1d, 64).ToArray(), new double[64], 1, 1, 1);
            for (int p = 0; p < 2; p++) for (int k = 0; k < 4; k++) for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
                Near(moved.Value(p, k, z * 8 + (x + 1) % 8), state.Value(p, k, z * 8 + x), 0);
        });
        Case("pure transform sliding preserves a straight material interface", () =>
        {
            var state = Slabs(16, false); var original = state;
            TectonicPlate[] plates = [new(0, 4, 8, 0, 1), new(1, 12, 8, 0, -1)];
            for (int step = 0; step < 32; step++)
            {
                var faces = state.EvaluateMotion(plates, 1, 1, 2).Faces();
                Require(faces.Divergence.All(v => v == 0), "Transform motion invented volume change.");
                state = state.Advect(faces.East, faces.South, 1, 1, .25);
            }
            var motion = state.EvaluateMotion(plates, 1, 1, 2);
            for (int i = 0; i < 256; i++)
            {
                Require(motion.Owners[i] == (i % 16 < 8 ? 0 : 1), "Transform motion relabelled material.");
                for (int p = 0; p < 2; p++) for (int k = 0; k < 4; k++) Near(state.Value(p, k, i), original.Value(p, k, i), 1e-11);
            }
        });
        Case("changing virtual seed centres cannot move an existing material boundary", () =>
        {
            var state = Slabs(8, false);
            TectonicPlate[] a = [new(0, 2, 4, 0, 1), new(1, 6, 4, 0, -1)];
            TectonicPlate[] b = [new(0, 400, -100, 0, 1), new(1, -200, 600, 0, -1)];
            var ma = state.EvaluateMotion(a, 1, 1, 1.5); var mb = state.EvaluateMotion(b, 1, 1, 1.5);
            Require(ma.X.SequenceEqual(mb.X) && ma.Z.SequenceEqual(mb.Z) && ma.Owners.SequenceEqual(mb.Owners), "Motion still depends on seed centres.");
        });
        Case("opposite periodic material contacts have opposite closure", () =>
        {
            var state = Slabs(8, false);
            TectonicPlate[] plates = [new(0, 2, 4, 1, 0), new(1, 6, 4, -1, 0)];
            var motion = state.EvaluateMotion(plates, 1, 1, 1.5);
            Require(motion.TryContact(3, true, plates, out var closing) && motion.TryContact(7, true, plates, out _), "Missing interface.");
            motion.TryContact(7, true, plates, out var opening);
            Near(closing.ClosingSpeed, 2, 1e-12); Near(opening.ClosingSpeed, -2, 1e-12);
            Near(closing.TangentialSpeed, 0, 1e-12);
        });
        Case("new basalt carries no invented age or inherited cohort", () =>
        {
            var state = Slabs(8, true); double[] born = new double[64]; born[6] = 2;
            var result = state.ExchangeOcean(born, Enumerable.Repeat(-1, 64).ToArray(), new double[64]);
            Near(result.Value(1, 1, 6), 9, 0); Near(result.Value(1, 2, 6), 350, 0); Near(result.Value(1, 3, 6), 7, 0);
            Near(state.Value(1, 1, 6), 7, 0);
        });
        Case("subduction removes only the selected origin and its own age moment", () =>
        {
            var state = Slabs(8, true).Advect(Enumerable.Repeat(1d, 64).ToArray(), new double[64], 1, 1, .5);
            int[] lower = Enumerable.Repeat(-1, 64).ToArray(); lower[4] = 1;
            double[] removed = new double[64]; removed[4] = 1;
            var result = state.ExchangeOcean(new double[64], lower, removed);
            for (int k = 0; k < 4; k++) Near(result.Value(0, k, 4), state.Value(0, k, 4), 0);
            Near(result.Value(1, 1, 4), 2.5, 0); Near(result.Value(1, 2, 4), 125, 1e-12); Near(result.Value(1, 3, 4), 2.5, 1e-12);
        });
        Case("continental redistribution preserves every origin inventory", () =>
        {
            int[] owners = Enumerable.Range(0, 64).Select(i => i % 8 < 4 ? 0 : 1).ToArray();
            double[] c = Enumerable.Repeat(35d, 64).ToArray(); c[27] = 80;
            var state = MaterialPlateCohorts.Create(8, 2, owners, c, new double[64], new double[64], new double[64]);
            var next = state.RelaxContinental(1, 1, .1, 1);
            double[] a = state.ContinentalInventories(), b = next.ContinentalInventories();
            for (int p = 0; p < 2; p++) Near(a[p], b[p], 1e-10);
            var expected = CrustTransport.RelaxThickCrust(c, 8, 1, 1, .1, 1);
            double[] actual = next.Aggregate()[0]; for (int i = 0; i < 64; i++) Near(expected[i], actual[i], 1e-10);
            Require(next.Value(0, 0, 28) > 0, "Origin did not follow spreading crust.");
        });
        Case("age moments remain attached to their ocean carriers", () =>
        {
            var state = Slabs(8, true).Advect(Enumerable.Repeat(.2, 64).ToArray(), Enumerable.Repeat(.1, 64).ToArray(), 1, 1, .5).Age(3);
            for (int i = 0; i < 64; i++) for (int p = 0; p < 2; p++)
            {
                Near(state.Value(p, 2, i), 53 * state.Value(p, 1, i), 1e-10);
                Require(state.Value(p, 3, i) <= state.Value(p, 1, i), "Inherited ocean exceeds its carrier.");
            }
        });
        Case("caller mutations cannot alter existing cohorts", () =>
        {
            int[] owners = new int[4]; double[] c = [35, 35, 35, 35];
            var state = MaterialPlateCohorts.Create(2, 1, owners, c, new double[4], new double[4], new double[4]);
            c[0] = 99; owners[0] = 99; var a = state.Aggregate(); a[0][0] = -5;
            Near(state.Value(0, 0, 0), 35, 0);
        });
        Case("invalid geometry, excessive CFL, absent carrier and excess removal are refused", () =>
        {
            var state = Slabs(8, true);
            Refuse(() => state.Advect(Enumerable.Repeat(2d, 64).ToArray(), new double[64], 1, 1, 1));
            Refuse(() => state.Age(double.NaN));
            Refuse(() => MaterialPlateCohorts.Create(513, 2, new int[1], new double[1], new double[1], new double[1], new double[1]));
            Refuse(() => MaterialPlateCohorts.Create(2, 1, new int[4], new double[4], new double[4], [1, 0, 0, 0], new double[4]));
            double[] removed = new double[64]; removed[6] = 8;
            Refuse(() => state.ExchangeOcean(new double[64], Enumerable.Repeat(1, 64).ToArray(), removed));
        });
        var scale = new TectonicScalePlan(1_000_000, 1_000_000);
        MaterialBoundHistory? history = null;
        Case("complete material history has the same starting crust as the baseline", () =>
        {
            var settings = new TectonicEvolutionSettings(side: 64, duration: 4);
            history = MaterialBoundHistory.Generate(73, scale, settings);
            var baseline = TectonicHistory.Generate(73, scale, settings);
            Require(history.Initial.ContinentalKm.SequenceEqual(baseline.Initial.ContinentalKm)
                && history.Initial.OceanicKm.SequenceEqual(baseline.Initial.OceanicKm)
                && history.Initial.OceanAge.SequenceEqual(baseline.Initial.OceanAge)
                && history.Initial.InheritedOceanicKm.SequenceEqual(baseline.Initial.InheritedOceanicKm)
                && history.Initial.ElevationKm.SequenceEqual(baseline.Initial.ElevationKm)
                && history.Initial.PlateIds.SequenceEqual(baseline.Initial.PlateIds), "Candidate changed the initial material state.");
            for (int p = 0; p < history.Plates.Count; p++)
                CrustTransport.RequireBalance(history.InitialContinentalByOrigin[p], history.FinalContinentalByOrigin[p], "test origin inventory");
        });
        Case("no-motion material history only thermally ages its oceanic crust", () =>
        {
            var still = MaterialBoundHistory.Generate(73, scale, new TectonicEvolutionSettings(side: 64, duration: 2, speedReferenceUnitsPerTime: 0));
            Require(still.Initial.ContinentalKm.SequenceEqual(still.Final.ContinentalKm), "Stationary crust changed.");
            Require(still.Ledger[^1].CreatedOceanicVolume == 0 && still.Ledger[^1].RecycledOceanicVolume == 0, "Stationary source or sink.");
        });
        Case("repeated material history and rescaled complete atlas are identical", () =>
        {
            var repeat = MaterialBoundHistory.Generate(73, new TectonicScalePlan(131072, 131072), new TectonicEvolutionSettings(side: 64, duration: 4));
            MaterialBoundHistory current = history ?? throw new InvalidOperationException("Missing fixture history.");
            Require(current.Checksum == repeat.Checksum, "Resizing changed the reference history.");
            foreach (long size in new long[] { 8192, 131072, 262144, 1_000_000 })
                for (int i = 0; i < 64; i++)
                {
                    double u = (i + .5) / 64, v = ((i * 17) % 64 + .5) / 64;
                    Near(current.SampleElevationKm(scale, u * 1e6, v * 1e6), current.SampleElevationKm(new TectonicScalePlan(size, size), u * size, v * size), 1e-11);
                }
        });
        Case("reversing motion changes the evolved field without changing initial materials", () =>
        {
            var reverse = MaterialBoundHistory.Generate(73, scale, new TectonicEvolutionSettings(side: 64, duration: 4, motionSign: -1));
            MaterialBoundHistory current = history ?? throw new InvalidOperationException("Missing fixture history.");
            Require(current.Initial.Checksum == reverse.Initial.Checksum && current.Final.Checksum != reverse.Final.Checksum, "No causal response to reversed motion.");
        });
        Case("material histories preserve rectangular metric geometry", () =>
        {
            var a = MaterialBoundHistory.Generate(73, new TectonicScalePlan(262144, 131072), new TectonicEvolutionSettings(side: 32, duration: 0));
            var b = MaterialBoundHistory.Generate(73, new TectonicScalePlan(1_000_000, 500000), new TectonicEvolutionSettings(side: 32, duration: 0));
            Require(a.Checksum == b.Checksum && a.ReferenceWidth == 1e6 && a.ReferenceLength == 500000, "Rectangular world cropped or stretched.");
        });
        return passed.ToArray();
        void Case(string name, Action action) { action(); passed.Add(name); Console.WriteLine("CHECK PASS: " + name); }
    }

    private static MaterialPlateCohorts Slabs(int n, bool oceanRight)
    {
        int[] owners = Enumerable.Range(0, n * n).Select(i => i % n < n / 2 ? 0 : 1).ToArray();
        double[] c = owners.Select(p => oceanRight && p == 1 ? 0d : 35d).ToArray();
        double[] o = owners.Select(p => oceanRight && p == 1 ? 7d : 0d).ToArray();
        return MaterialPlateCohorts.Create(n, 2, owners, c, o, o.Select(v => v * 50).ToArray(), o);
    }
    private static void Near(double a, double b, double tolerance) => Require(Math.Abs(a - b) <= tolerance, $"Expected {a:R}, actual {b:R}.");
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Refuse(Action action)
    {
        try { action(); } catch (ArgumentException) { return; } catch (ArithmeticException) { return; }
        throw new InvalidOperationException("Invalid input accepted.");
    }
}
