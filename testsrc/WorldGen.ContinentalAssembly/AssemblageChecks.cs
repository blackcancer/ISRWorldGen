using ISRWorldGen.Core.Geology.Evolution;

internal static class AssemblageChecks
{
    internal static string[] Run()
    {
        var cases = new List<string>();
        var scale = new TectonicScalePlan(1_000_000, 1_000_000);
        var a = ContinentalAssemblage.Generate(-437287116, scale, 64, 12);
        Case("initial continental volume is prescribed, not a random plate weight", () =>
            Near(CrustTransport.Sum(a.ContinentalKm), 12 * 64 * 64, 1e-7));
        Case("materials are finite, nonnegative and every column has a carrier", () =>
        {
            for (int i = 0; i < a.ContinentalKm.Count; i++)
                Require(double.IsFinite(a.ContinentalKm[i]) && a.ContinentalKm[i] >= 0 &&
                    double.IsFinite(a.OceanicKm[i]) && a.OceanicKm[i] >= 0 && a.ContinentalKm[i] + a.OceanicKm[i] > 0, "invalid column");
        });
        Case("seeded provinces have unequal physical area targets", () =>
        {
            Near(a.Provinces.Sum(p => p.TargetAreaFraction), .36, 1e-14);
            Require(a.Provinces.Max(p => p.TargetAreaFraction) / a.Provinces.Min(p => p.TargetAreaFraction) > 1.5, "equal allocation returned");
            foreach (var p in a.Provinces) Require(p.ActualAreaFraction >= .8 * p.TargetAreaFraction, "inaccessible quota");
        });
        Case("repeated initial material generation is deterministic", () =>
            Require(a.Checksum == ContinentalAssemblage.Generate(-437287116, scale, 64, 12).Checksum, "repeat differs"));
        Case("smaller square worlds contain the SAME COMPLETE material atlas", () =>
        {
            foreach (long size in new[] { 131072L, 262144L, 1000000L })
            {
                var b = ContinentalAssemblage.Generate(-437287116, new TectonicScalePlan(size, size), 64, 12);
                Require(a.ContinentalKm.SequenceEqual(b.ContinentalKm) && a.OceanicKm.SequenceEqual(b.OceanicKm) &&
                    a.ProvinceIds.SequenceEqual(b.ProvinceIds), "world resize cropped or changed geology");
            }
        });
        Case("terrane graph and province targets do not depend on raster resolution", () =>
        {
            var b = ContinentalAssemblage.Generate(-437287116, scale, 128, 12);
            for (int p = 0; p < a.Provinces.Count; p++)
            {
                Require(a.Provinces[p].NucleusSite == b.Provinces[p].NucleusSite, "resolution changed initial graph");
                Near(a.Provinces[p].ActualAreaFraction, b.Provinces[p].ActualAreaFraction, 0);
            }
        });
        Case("province count is seed-controlled and independent of plate count", () =>
        {
            var counts = new HashSet<int>();
            foreach (int seed in new[] { -437287116, 73, 20260906, 0, 1, 2 })
            {
                var b = ContinentalAssemblage.Generate(seed, scale, 32);
                Require(b.Provinces.Count is >= 3 and <= 6, "invalid count"); counts.Add(b.Provinces.Count);
            }
            Require(counts.Count > 1, "all seeds forced to one count");
        });
        Case("rectangular complete atlases have explicit geometric support", () =>
        {
            foreach (var s in new[] { new TectonicScalePlan(1000000, 500000), new TectonicScalePlan(250000, 1000000) })
            {
                var b = ContinentalAssemblage.Generate(73, s, 64);
                Near(b.ReferenceWidth, s.ReferenceWidth, 0); Near(b.ReferenceLength, s.ReferenceLength, 0);
                Near(CrustTransport.Sum(b.ContinentalKm), 12.6 * 4096, 1e-7);
            }
        });
        Case("invalid initial budgets fail before generation", () =>
        {
            Refuse(() => ContinentalAssemblage.Generate(1, scale, 63));
            Refuse(() => ContinentalAssemblage.Generate(1, scale, 64, double.NaN));
            Refuse(() => ContinentalAssemblage.Generate(1, scale, 64, 12, 7));
            Refuse(() => ContinentalAssemblage.Generate(1, scale, 64, 0));
        });
        Case("null initial material is never a silent legacy fallback", () =>
            Refuse(() => MaterialBoundHistory.GenerateWithAssemblage(1, scale, new(side: 64, duration: 0), null!)));
        Case("wrong seed or raster cannot reattribute initial material", () =>
        {
            Refuse(() => MaterialBoundHistory.GenerateWithAssemblage(1, scale, new(side: 64, duration: 0), a));
            Refuse(() => MaterialBoundHistory.GenerateWithAssemblage(a.Seed, scale, new(side: 128, duration: 0), a));
            Refuse(() => MaterialBoundHistory.GenerateWithAssemblage(a.Seed, new(1000000, 500000), new(side: 64, duration: 0), a));
        });
        Case("zero-duration history preserves exact initial columns", () =>
        {
            var history = MaterialBoundHistory.GenerateWithAssemblage(a.Seed, scale, new(side: 64, duration: 0), a);
            Require(history.Initial.ContinentalKm.SequenceEqual(a.ContinentalKm), "initial C changed");
            Require(history.Initial.OceanicKm.SequenceEqual(a.OceanicKm), "initial O changed");
            Require(history.Initial.ElevationKm.SequenceEqual(history.Final.ElevationKm), "zero duration changed relief");
            Require(history.InitialAssemblageChecksum == a.Checksum, "initial provenance lost");
        });
        Case("mechanical plate count does not set initial continental material", () =>
        {
            var h = MaterialBoundHistory.GenerateWithAssemblage(a.Seed, scale, new(side: 64, plateCount: 8, duration: 0), a);
            Require(h.Plates.Count == 8 && h.Initial.ContinentalKm.SequenceEqual(a.ContinentalKm), "one continent per plate");
        });
        Case("legacy generator remains explicitly selectable", () =>
        {
            var h = MaterialBoundHistory.Generate(a.Seed, scale, new(side: 64, duration: 0));
            Require(h.InitialAssemblageChecksum == "LEGACY_EQUAL_QUOTA_INITIALIZATION", "legacy silently replaced");
            Require(!h.Initial.ContinentalKm.SequenceEqual(a.ContinentalKm), "candidate not injected");
        });
        Case("material budget matching and initial arrays survive history generation", () =>
        {
            string before = a.Checksum; double[] values = a.ContinentalKm.ToArray();
            var h = MaterialBoundHistory.GenerateWithAssemblage(a.Seed, scale, new(side: 64, duration: 2), a);
            Near(h.Ledger[0].ContinentalVolume, h.Ledger[^1].ContinentalVolume, h.Ledger[0].ContinentalVolume * 1e-10);
            Require(a.Checksum == before && a.ContinentalKm.SequenceEqual(values), "initial state mutated");
        });
        Case("frozen fields do not expose mutable array references", () =>
        {
            try { ((IList<double>)a.ContinentalKm)[0] = 999; }
            catch (NotSupportedException) { return; }
            throw new InvalidOperationException("mutable material snapshot");
        });
        return cases.ToArray();
        void Case(string label, Action test) { test(); cases.Add(label); Console.WriteLine("CHECK PASS: " + label); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double a, double b, double tolerance) => Require(Math.Abs(a - b) <= tolerance, $"{a:R} != {b:R}");
    private static void Refuse(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected rejection."); }
}
