using ISRWorldGen.Core.Geology.Evolution;

internal static class MaterialDomainChecks
{
    public static string[] Run()
    {
        var passed = new List<string>();
        var a = new TectonicPlate(0, 4, 8, 1, 0);
        var b = new TectonicPlate(1, 12, 8, -1, 0);
        var d = AdvectedPlateDomains.FromVoronoi([a, b], 16, 16, 16, 1);
        Case("carrier fractions are finite and complete", () =>
        {
            for (int i = 0; i < 256; i++)
            {
                Near(d.Fraction(0, i) + d.Fraction(1, i), 1, 1e-14);
                Require(d.Fraction(0, i) >= 0 && d.Fraction(1, i) >= 0 && d.Confidence(i) >= .5 - 1e-12, "invalid ownership");
                Near(d.Density(i), 1, 1e-14);
            }
        });
        Case("input order cannot change plate coordinates", () =>
        {
            var reversed = AdvectedPlateDomains.FromVoronoi([b, a], 16, 16, 16, 1);
            Same(d, reversed);
        });
        Case("input collections cannot mutate the frozen carrier", () =>
        {
            TectonicPlate[] source = [a, b];
            var frozen = AdvectedPlateDomains.FromVoronoi(source, 16, 16, 16, 1);
            source[0] = source[1]; Same(d, frozen);
            bool refused = false;
            try { ((IList<double>)frozen.Inventory)[0] = 0; } catch (NotSupportedException) { refused = true; }
            Require(refused, "mutable inventory");
        });
        Case("zero face flux cannot reassign plate ownership even over long elapsed time", () =>
        {
            // The old moving-site partition would change despite these zero
            // material fluxes. The carrier only changes through supplied fluxes.
            var next = d.Advect(new double[256], new double[256], 100);
            Same(d, next);
        });
        Case("exact east translation moves every plate coordinate one cell", () =>
        {
            var moved = d.Advect(Enumerable.Repeat(1d, 256).ToArray(), new double[256], 1);
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) for (int p = 0; p < 2; p++)
                Near(moved.Fraction(p, z * 16 + (x + 1) % 16), d.Fraction(p, z * 16 + x), 0);
            Same(d, AdvectedPlateDomains.FromVoronoi([a, b], 16, 16, 16, 1));
        });
        Case("rectangular metrics retain exact south and west translations", () =>
        {
            var rect = AdvectedPlateDomains.FromVoronoi([a, b], 16, 16, 32, 1);
            var moved = rect.Advect(new double[256], Enumerable.Repeat(2d, 256).ToArray(), 1);
            var west = rect.Advect(Enumerable.Repeat(-1d, 256).ToArray(), new double[256], 1);
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) for (int p = 0; p < 2; p++)
            {
                Near(moved.Fraction(p, ((z + 1) % 16) * 16 + x), rect.Fraction(p, z * 16 + x), 0);
                Near(west.Fraction(p, z * 16 + (x + 15) % 16), rect.Fraction(p, z * 16 + x), 0);
            }
        });
        Case("compressible transport conserves each carrier, not each normalized fraction", () =>
        {
            double[] east = Enumerable.Range(0, 256).Select(i => .3 * Math.Sin(i % 16)).ToArray();
            var moved = d.Advect(east, new double[256], .5);
            for (int k = 0; k < 2; k++) Near(d.Inventory[k], moved.Inventory[k], 1e-11);
            Require(Enumerable.Range(0, 256).Any(i => Math.Abs(moved.Density(i) - 1) > .01), "carrier incorrectly normalized after transport");
            for (int i = 0; i < 256; i++) Near(moved.Fraction(0, i) + moved.Fraction(1, i), 1, 1e-14);
        });
        Case("carrier velocities stay in the prescribed convex velocity envelope", () =>
        {
            for (int i = 0; i < 256; i++)
            {
                var v = d.Velocity(i); Require(v.X >= -1 && v.X <= 1 && v.Z == 0, "velocity exceeds CFL bound");
            }
        });
        Case("opposite material contacts have opposite closure signs", () =>
        {
            Near(d.ClosingSpeedAtFace(8 * 16 + 7, 0), 2, 1e-12);
            Near(d.ClosingSpeedAtFace(8 * 16 + 15, 0), -2, 1e-12);
            Require(d.ClosingSpeedAtFace(8 * 16 + 3, 0) == 0, "intraplate contact invented");
        });
        Case("axis exchange retains contact closure and transported geometry", () =>
        {
            var rotated = AdvectedPlateDomains.FromVoronoi([new(0, 8, 4, 0, 1), new(1, 8, 12, 0, -1)], 16, 16, 16, 1);
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                Near(d.ClosingSpeedAtFace(z * 16 + x, 0), rotated.ClosingSpeedAtFace(x * 16 + z, 1), 1e-11);
        });
        Case("invalid carrier inputs and unsafe CFL are rejected without mutating prior state", () =>
        {
            Refuse(() => AdvectedPlateDomains.FromVoronoi([a, a], 16, 16, 16, 1));
            Refuse(() => AdvectedPlateDomains.FromVoronoi([a, b], 16, double.NaN, 16, 1));
            Refuse(() => AdvectedPlateDomains.FromVoronoi([a, b], 513, 16, 16, 1));
            Refuse(() => d.Advect(Enumerable.Repeat(2d, 256).ToArray(), new double[256], 1));
            Refuse(() => d.Advect(new double[255], new double[256], 0));
            Refuse(() => d.Fraction(-1, 0)); Refuse(() => d.ClosingSpeedAtFace(0, 2));
            Same(d, AdvectedPlateDomains.FromVoronoi([a, b], 16, 16, 16, 1));
        });
        Case("concurrent carrier reads have no shared mutable state", () =>
            Parallel.For(0, 256, i => { Near(d.Density(i), 1, 1e-14); Require(d.Owner(i) == d.Owner(i), "owner changed"); }));
        Case("metric coupling preserves common translation and zero forcing", () =>
        {
            foreach (double value in new[] { 0d, 2.5, -3d })
            {
                var result = PlateVelocityCoherence.Apply(Enumerable.Repeat(value, 256).ToArray(), 16, 2, 3, 4.25);
                foreach (double v in result) Near(v, value, 1e-13);
            }
        });
        Case("grid-scale velocity shocks are attenuated without changing the mean", () =>
        {
            double[] signal = Enumerable.Range(0, 256).Select(i => i % 2 == 0 ? 1d : -1d).ToArray();
            var result = PlateVelocityCoherence.Apply(signal, 16, 1, 1, 2);
            Require(result.Max(v => Math.Abs(v)) < .1, "unresolved shock retained");
            Near(CrustTransport.Sum(result), 0, 1e-12);
            Require(signal[0] == 1 && signal[1] == -1, "forcing input changed");
        });
        Case("metric coupling commutes with periodic translation and bounds velocities", () =>
        {
            double[] signal = Enumerable.Range(0, 256).Select(i => Math.Sin(i * .37)).ToArray();
            double[] shifted = Enumerable.Range(0, 256).Select(i => signal[(i / 16) * 16 + (i % 16 + 15) % 16]).ToArray();
            var r = PlateVelocityCoherence.Apply(signal, 16, 2, 3, 5.25);
            var t = PlateVelocityCoherence.Apply(shifted, 16, 2, 3, 5.25);
            for (int i = 0; i < 256; i++) Near(t[i], r[(i / 16) * 16 + (i % 16 + 15) % 16], 1e-12);
            Require(r.Min() >= signal.Min() - 1e-12 && r.Max() <= signal.Max() + 1e-12, "forcing overshoot");
        });
        Case("invalid metric coupling cannot silently filter terrain", () =>
        {
            Refuse(() => PlateVelocityCoherence.Apply(new double[256], 16, 0, 1, 2));
            Refuse(() => PlateVelocityCoherence.Apply(new double[256], 16, 1, 1, double.NaN));
            Refuse(() => PlateVelocityCoherence.Apply(new double[255], 16, 1, 1, 2));
        });
        var settings = new TectonicEvolutionSettings(side: 64, duration: 8, advectPlateDomains: true);
        var scale = new TectonicScalePlan(1_000_000, 1_000_000);
        TectonicHistory? material = null;
        Case("complete material-domain history conserves crust and every plate carrier", () =>
        {
            material = TectonicHistory.Generate(73, scale, settings);
            var first = material.Ledger[0]; var last = material.Ledger[^1];
            CrustTransport.RequireBalance(first.ContinentalVolume, last.ContinentalVolume, "material domain continental balance");
            CrustTransport.RequireBalance(first.OceanicVolume + last.CreatedOceanicVolume - last.RecycledOceanicVolume,
                last.OceanicVolume, "material domain oceanic balance");
            Require(material.InitialCarrierInventory.Count == settings.PlateCount, "carrier ledger absent");
            for (int k = 0; k < settings.PlateCount; k++)
                CrustTransport.RequireBalance(material.InitialCarrierInventory[k], material.FinalCarrierInventory[k], "material domain carrier");
            Require(material.Final.ElevationKm.All(double.IsFinite), "invalid material elevation");
        });
        Case("advected domains change actual height history without changing initial crust", () =>
        {
            var reference = TectonicHistory.Generate(73, scale, new TectonicEvolutionSettings(side: 64, duration: 8));
            Require(material!.Initial.ContinentalKm.SequenceEqual(reference.Initial.ContinentalKm)
                && material.Initial.ElevationKm.SequenceEqual(reference.Initial.ElevationKm), "comparison uses different initial crust");
            Require(!material.Final.ElevationKm.SequenceEqual(reference.Final.ElevationKm), "domain change is only an image change");
        });
        Case("repeat and complete-world resizing preserve the same advected history", () =>
        {
            var repeat = TectonicHistory.Generate(73, new TectonicScalePlan(131072, 131072), settings);
            Require(material!.Checksum == repeat.Checksum, "material domains depend on output map size");
            Near(material.SampleElevationKm(scale, 250000, 750000), repeat.SampleElevationKm(new TectonicScalePlan(131072, 131072), 32768, 98304), 1e-12);
        });
        Case("stationary material domains create no deformation or sources", () =>
        {
            var still = TectonicHistory.Generate(73, scale, new TectonicEvolutionSettings(side: 64, duration: 2, speedReferenceUnitsPerTime: 0, advectPlateDomains: true));
            Require(still.Initial.ContinentalKm.SequenceEqual(still.Final.ContinentalKm)
                && still.Initial.OceanicKm.SequenceEqual(still.Final.OceanicKm)
                && still.Initial.PlateIds.SequenceEqual(still.Final.PlateIds), "stationary carrier moved");
            Require(still.Ledger[^1].CreatedOceanicVolume == 0 && still.Ledger[^1].RecycledOceanicVolume == 0, "stationary source or sink");
        });
        return passed.ToArray();
        void Case(string name, Action action) { action(); passed.Add(name); Console.WriteLine("CHECK PASS: " + name); }
    }
    private static void Same(AdvectedPlateDomains a, AdvectedPlateDomains b)
    {
        for (int i = 0; i < a.Side * a.Side; i++)
        {
            Require(a.Owner(i) == b.Owner(i) && a.Velocity(i) == b.Velocity(i), "changed immutable domain");
            for (int k = 0; k < a.PlateCount; k++) Near(a.Fraction(k, i), b.Fraction(k, i), 0);
        }
    }
    private static void Near(double a, double b, double tol) => Require(double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= tol, $"{a:R} != {b:R}");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Refuse(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid carrier input accepted"); }
}
