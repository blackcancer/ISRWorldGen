using ISRWorldGen.Core.Geology.Evolution;

internal static class TectonicChecks
{
    public static string[] Run()
    {
        var passed = new List<string>();
        Case("full-atlas scale preserves all normalized coordinates", () =>
        {
            foreach (long size in new long[] { 8192, 131072, 262144, 1_000_000, 1_024_000 })
            {
                var scale = new TectonicScalePlan(size, size);
                Require(scale.ReferenceWidth == 1_000_000 && scale.ReferenceLength == 1_000_000, "cropped tectonic reference");
                var p = scale.ToReference(size / 4d, size * .75);
                Near(p.X, 250000, 1e-8); Near(p.Z, 750000, 1e-8);
            }
        });
        Case("rectangular worlds preserve metric aspect without cropping", () =>
        {
            var a = new TectonicScalePlan(1_000_000, 500000); var b = new TectonicScalePlan(262144, 131072);
            Near(a.ReferenceWidth, b.ReferenceWidth, 1e-8); Near(a.ReferenceLength, b.ReferenceLength, 1e-8);
            Require(a.ReferenceWidth / a.ReferenceLength == 2, "anisotropic stretch");
        });
        Case("invalid scale and evolution configurations are refused", () =>
        {
            Refuse(() => new TectonicScalePlan(0, 100000)); Refuse(() => new TectonicScalePlan(1_000_000, 8192));
            Refuse(() => new TectonicScalePlan(131072, 131072).ToReference(131072, 0));
            Refuse(() => new TectonicScalePlan(131072, 131072).ToReference(double.NaN, 1));
            Refuse(() => new TectonicEvolutionSettings(side: 33)); Refuse(() => new TectonicEvolutionSettings(duration: double.NaN));
            Refuse(() => new TectonicEvolutionSettings(motionSign: 0));
        });
        Case("constant translation advects a pulse exactly one cell", () =>
        {
            double[] q = new double[16]; q[5] = 7;
            double[] moved = CrustTransport.Advect(q, Enumerable.Repeat(1d, 16).ToArray(), new double[16], 4, 1, 1, 1);
            Require(moved[6] == 7 && CrustTransport.Sum(moved) == 7 && q[5] == 7, "translation or input mutation");
        });
        Case("periodic transport preserves positive mass", () =>
        {
            double[] q = Enumerable.Range(0, 64).Select(i => (i % 13) / 7d).ToArray();
            double[] vx = Enumerable.Range(0, 64).Select(i => Math.Sin(i) * .4).ToArray();
            double[] vz = Enumerable.Range(0, 64).Select(i => Math.Cos(i) * .4).ToArray();
            double[] result = CrustTransport.Advect(q, vx, vz, 8, 2, 3, .8);
            Near(CrustTransport.Sum(result), CrustTransport.Sum(q), 1e-12); Require(result.All(v => v >= 0), "negative volume");
        });
        Case("oversized CFL, negative quantities and malformed fields are refused", () =>
        {
            Refuse(() => CrustTransport.Advect(new double[16], Enumerable.Repeat(2d, 16).ToArray(), new double[16], 4, 1, 1, 1));
            Refuse(() => CrustTransport.Advect([-1, 0, 0, 0], new double[4], new double[4], 2, 1, 1, .1));
            Refuse(() => CrustTransport.Advect(new double[4], new double[3], new double[4], 2, 1, 1, .1));
        });
        Case("pure shear does not create crust thickness", () =>
        {
            double[] uniform = Enumerable.Repeat(35d, 64).ToArray();
            double[] vx = Enumerable.Range(0, 64).Select(i => (i / 8 - 3) * .1).ToArray();
            double[] result = CrustTransport.Advect(uniform, vx, new double[64], 8, 1, 1, .5);
            Require(result.SequenceEqual(uniform), "shear painted uplift");
        });
        Case("reversing a convergence reverses thickening and extension", () =>
        {
            double[] q = Enumerable.Repeat(35d, 64).ToArray();
            double[] vx = Enumerable.Range(0, 64).Select(i => i % 8 < 4 ? .5 : -.5).ToArray();
            var a = CrustTransport.Advect(q, vx, new double[64], 8, 1, 1, .5);
            var b = CrustTransport.Advect(q, vx.Select(v => -v).ToArray(), new double[64], 8, 1, 1, .5);
            Require(a[4] > 35 && b[4] < 35 && a[0] < 35 && b[0] > 35, "kinematic response is not causal");
            Near(CrustTransport.Sum(a), CrustTransport.Sum(b), 1e-12);
        });
        Case("lower-crust spreading conserves volume without erosion", () =>
        {
            double[] q = Enumerable.Repeat(35d, 64).ToArray(); q[27] = 80;
            var r = CrustTransport.RelaxThickCrust(q, 8, 1, 1, .1, 1);
            Require(r[27] < q[27] && r[26] > q[26] && r.All(v => v >= 0), "no tectonic redistribution");
            Near(CrustTransport.Sum(q), CrustTransport.Sum(r), 1e-12);
            Refuse(() => CrustTransport.RelaxThickCrust(q, 8, 1, 1, 1, 1));
        });
        Case("continental collision has no arbitrarily sinking continent", () =>
            Require(CrustResponse.Choose(1, 35, 0, 0, 2, 35, 0, 0).Kind == TectonicContactKind.ContinentalCollision, "continental subduction"));
        Case("ocean-continent polarity is invariant under label exchange", () =>
        {
            var a = CrustResponse.Choose(1, 0, 7, 80, 2, 35, 0, 0);
            var b = CrustResponse.Choose(2, 35, 0, 0, 1, 0, 7, 80);
            Require(a == b && a.SubductingPlate == 1 && a.OverridingPlate == 2, "polarity changed with order");
        });
        Case("opposite periodic contacts have opposite closure and preserve label symmetry", () =>
        {
            var a = new TectonicPlate(1, 25, 50, 1, 0); var b = new TectonicPlate(2, 75, 50, -1, 0);
            Near(CrustResponse.ClosingSpeed(a, b, 50, 50, 100, 100, 0), 2, 1e-12);
            Near(CrustResponse.ClosingSpeed(a, b, 0, 50, 100, 100, 0), -2, 1e-12);
            Near(CrustResponse.ClosingSpeed(b, a, 50, 50, 100, 100, 0), 2, 1e-12);
            Refuse(() => CrustResponse.ClosingSpeed(a, a, 0, 0, 100, 100, 0));
        });
        Case("older oceanic lithosphere determines symmetric contact polarity", () =>
        {
            var a = CrustResponse.Choose(1, 0, 7, 10, 2, 0, 7, 80);
            var b = CrustResponse.Choose(2, 0, 7, 80, 1, 0, 7, 10);
            Require(a == b && a.SubductingPlate == 2, "age ignored");
        });
        Case("thickening raises continental crust and ageing deepens the solid seabed", () =>
        {
            Require(CrustResponse.ElevationKm(55, 0, 0) > CrustResponse.ElevationKm(35, 0, 0), "no isostasy");
            Require(CrustResponse.ElevationKm(0, 7, 80) < CrustResponse.ElevationKm(0, 7, 0), "no thermal subsidence");
            Require(CrustResponse.ElevationKm(0, 7, 0) < 0, "young seafloor is a water surface");
            Refuse(() => CrustResponse.ElevationKm(double.NaN, 7, 10));
        });
        var scale = new TectonicScalePlan(1_000_000, 1_000_000);
        TectonicHistory? still = null;
        Case("stationary history creates no basalt, recycling or fictitious mountains", () =>
        {
            still = TectonicHistory.Generate(73, scale, new TectonicEvolutionSettings(side: 64, duration: 2, speedReferenceUnitsPerTime: 0));
            Require(still.Initial.ContinentalKm.SequenceEqual(still.Final.ContinentalKm), "stationary continental deformation");
            Require(still.Initial.OceanicKm.SequenceEqual(still.Final.OceanicKm), "stationary oceanic deformation");
            Require(still.Ledger[^1].CreatedOceanicVolume == 0 && still.Ledger[^1].RecycledOceanicVolume == 0, "unjustified source/sink");
            for (int i = 0; i < still.Final.OceanicKm.Count; i++) if (still.Final.OceanicKm[i] > 0) Near(still.Final.OceanAge[i], 52, 1e-10);
        });
        Case("frozen snapshots resist external mutation", () =>
        {
            bool refused = false;
            try { ((IList<double>)still!.Final.OceanicKm)[0] = -1; } catch (NotSupportedException) { refused = true; }
            Require(refused, "mutable snapshot");
        });
        TectonicHistory? motion = null;
        Case("evolving history conserves continental inventory and accounts for oceanic sources and sinks", () =>
        {
            motion = TectonicHistory.Generate(73, scale, new TectonicEvolutionSettings(side: 64, duration: 8));
            var first = motion.Ledger[0]; var last = motion.Ledger[^1];
            Require(last.CreatedOceanicVolume > 0 && last.RecycledOceanicVolume > 0, "fixture did not exercise both processes");
            CrustTransport.RequireBalance(first.ContinentalVolume, last.ContinentalVolume, "test continental inventory");
            CrustTransport.RequireBalance(first.OceanicVolume + last.CreatedOceanicVolume - last.RecycledOceanicVolume, last.OceanicVolume, "test ocean inventory");
            Require(!motion.Initial.ElevationKm.SequenceEqual(motion.Final.ElevationKm), "history did not change terrain");
            for (int i = 0; i < motion.Final.OceanicKm.Count; i++)
                Require(motion.Final.InheritedOceanicKm[i] <= motion.Final.OceanicKm[i] + 1e-10, "inherited cohort grew from new basalt");
        });
        Case("rebuilding a history is deterministic", () =>
        {
            var repeat = TectonicHistory.Generate(73, scale, new TectonicEvolutionSettings(side: 64, duration: 8));
            Require(motion!.Checksum == repeat.Checksum && motion.Final.ElevationKm.SequenceEqual(repeat.Final.ElevationKm), "nonrepeatable history");
        });
        Case("small maps sample the same complete plate atlas, not its central continent", () =>
        {
            foreach (long size in new long[] { 8192, 131072, 262144, 1_000_000 })
            {
                var resized = new TectonicScalePlan(size, size);
                for (int j = 0; j < 16; j++) for (int i = 0; i < 16; i++)
                {
                    double u = (i + .5) / 16, v = (j + .5) / 16;
                    Near(motion!.SampleElevationKm(scale, u * 1_000_000, v * 1_000_000), motion.SampleElevationKm(resized, u * size, v * size), 1e-11);
                }
            }
            Refuse(() => motion!.SampleElevationKm(new TectonicScalePlan(262144, 131072), 1, 1));
        });
        Case("regenerating a resized square world preserves the entire material history", () =>
        {
            var resized = TectonicHistory.Generate(73, new TectonicScalePlan(131072, 131072), new TectonicEvolutionSettings(side: 64, duration: 8));
            Require(motion!.Checksum == resized.Checksum, "world dimensions cropped or changed the tectonic history");
        });
        Case("reversing prescribed plate motion changes the history but not its initial materials", () =>
        {
            var opposite = TectonicHistory.Generate(73, scale, new TectonicEvolutionSettings(side: 64, duration: 8, motionSign: -1));
            Require(motion!.Initial.Checksum == opposite.Initial.Checksum, "motion sign changed initial crust");
            Require(motion.Final.Checksum != opposite.Final.Checksum, "history ignored reversed motion");
        });
        Case("concurrent read-only sampling preserves values", () =>
        {
            double[] values = Enumerable.Range(0, 128).Select(i => motion!.SampleElevationKm(scale, i * 7013 % 1_000_000, i * 3037 % 1_000_000)).ToArray();
            Parallel.For(0, 128, i => Require(values[i] == motion!.SampleElevationKm(scale, i * 7013 % 1_000_000, i * 3037 % 1_000_000), "concurrent mutation"));
        });
        return passed.ToArray();
        void Case(string name, Action action) { action(); passed.Add(name); Console.WriteLine("CHECK PASS: " + name); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double a, double b, double tolerance) => Require(Math.Abs(a - b) <= tolerance, "Numerical mismatch: " + a + " vs " + b);
    private static void Refuse(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid input accepted");
    }
}
