using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

internal static class PrehistoryChecks
{
    public static void Run(Action<string, Action> check, string? output)
    {
        const int seed = 73, n = 32;
        var scale = new TectonicScalePlan(1_000_000, 1_000_000);
        var assemblage = ContinentalAssemblage.Generate(seed, scale, n);
        TectonicEvolutionSettings Settings(double t, double speed = 1800) => new(side: n, duration: t,
            speedReferenceUnitsPerTime: speed, lowerCrustMobility: 0);
        // n=32 and these rates yield dt=1. The split equality checks BELOW are
        // intentionally at exact numerical step boundaries, not arbitrary times.
        var start = MaterialBoundHistory.BeginPrehistory(seed, scale, Settings(0), assemblage);
        var first = start.ContinuePrehistory(scale, 1);
        var split = first.ContinuePrehistory(scale, 2);
        var whole = MaterialBoundHistory.BeginPrehistory(seed, scale, Settings(3), assemblage);
        check("prehistory retention is explicit and legacy path does not retain arrays", () =>
        {
            var legacy = MaterialBoundHistory.GenerateWithAssemblage(seed, scale, Settings(3), assemblage);
            Require(!legacy.CanContinuePrehistory && whole.CanContinuePrehistory, "Unexpected material retention.");
            Require(legacy.Checksum == whole.Checksum && legacy.FinalMaterialChecksum == whole.FinalMaterialChecksum, "Retaining state changed legacy calculation.");
            Refuse(() => legacy.ContinuePrehistory(scale, 1));
        });
        check("aligned phases reproduce an uninterrupted material history exactly", () =>
        {
            Require(split.FinalMaterialChecksum == whole.FinalMaterialChecksum, "Phase split changed the full origin state.");
            Require(split.Final.Checksum == whole.Final.Checksum, "Split changed altitude/age/strain/timestamp.");
            Require(split.CompletedPrehistorySteps == whole.CompletedPrehistorySteps && whole.CompletedPrehistorySteps == 3, "Step count reset.");
        });
        check("continuation records exact material and history lineage", () =>
        {
            Require(split.InitialMaterialChecksum == first.FinalMaterialChecksum, "Material was rebuilt.");
            Require(split.ContinuationParentChecksum == first.Checksum, "Missing parent identity.");
            Require(split.Checksum != whole.Checksum, "Different computation provenance was erased.");
            Near(split.Initial.Time, 1); Near(split.Final.Time, 3);
        });
        check("mixed origins survive continuation instead of dominant-owner reconstruction", () =>
        {
            Require(first.FinalOwnerFraction.Any(x => x < .999999), "Fixture did not contain mixed origins.");
            Require(split.FinalMaterialChecksum == whole.FinalMaterialChecksum, "Mixed origin quantities were lost.");
        });
        check("zero duration is a state-preserving continuation", () =>
        {
            var zero = first.ContinuePrehistory(scale, 0);
            Require(zero.FinalMaterialChecksum == first.FinalMaterialChecksum && zero.Final.Checksum == first.Final.Checksum, "Zero step changed state.");
            Require(zero.CompletedPrehistorySteps == first.CompletedPrehistorySteps, "Zero step added work.");
        });
        check("branching two continuations cannot mutate the parent", () =>
        {
            string fingerprint = first.FinalMaterialChecksum, snapshot = first.Final.Checksum;
            var a = first.ContinuePrehistory(scale, 2); var b = first.ContinuePrehistory(scale, 2);
            Require(a.Checksum == b.Checksum && first.FinalMaterialChecksum == fingerprint && first.Final.Checksum == snapshot, "Shared state mutated.");
        });
        var stationary = MaterialBoundHistory.BeginPrehistory(seed, scale, Settings(1, 0), assemblage);
        var older = stationary.ContinuePrehistory(scale, 2);
        check("stationary ocean keeps age and ages across a phase boundary", () =>
        {
            for (int i = 0; i < n * n; i++) if (older.Final.OceanicKm[i] > 1e-100)
                Near(older.Final.OceanAge[i], 53, 1e-9);
            Require(older.Final.ContinentalKm.SequenceEqual(stationary.Final.ContinentalKm), "Stationary continent moved.");
        });
        check("surviving inherited ocean cannot be declared a reconstructed prehistory", () =>
        {
            var status = OceanPrehistory.Describe(older);
            Require(status.HasUniformAgePriorWithoutBirthEvidence && status.Status == "INCOMPLETE_INHERITED_FORMATION_HISTORY", "Unknown source history hidden.");
            Near(status.InheritedCarrierFraction!.Value, 1, 1e-12);
        });
        check("coverage accounting uses volume not cell count or sea threshold", () =>
        {
            var status = OceanPrehistory.Describe(split);
            double ratio = CrustTransport.Sum(split.Final.InheritedOceanicKm) / CrustTransport.Sum(split.Final.OceanicKm);
            Near(status.InheritedCarrierFraction!.Value, ratio, 0);
            CrustTransport.RequireBalance(status.OceanicVolumeKm3, status.InheritedVolumeKm3 + status.FormedSinceStartVolumeKm3, "coverage accounting");
        });
        check("forcing changes preserve birth ages and do not move origin sites", () =>
        {
            var motion = first.Plates.Select(p => new OriginMotion(p.Id, -p.Vx, -p.Vz)).ToArray();
            var changed = first.ContinuePrehistory(scale, 0, motion);
            Require(changed.FinalMaterialChecksum == first.FinalMaterialChecksum, "Forcing change reset material.");
            for (int i = 0; i < motion.Length; i++)
            { Near(changed.Plates[i].Vx, -first.Plates[i].Vx, 0); Near(changed.Plates[i].X, first.Plates[i].X, 0); }
            Require(changed.Checksum != first.ContinuePrehistory(scale, 0).Checksum, "Forcing missing from identity.");
        });
        check("origin forcing order has a canonical identity", () =>
        {
            var motion = first.Plates.Select(p => new OriginMotion(p.Id, p.Vx, p.Vz)).ToArray();
            Require(first.ContinuePrehistory(scale, 0, motion).Checksum == first.ContinuePrehistory(scale, 0, motion.Reverse().ToArray()).Checksum, "Input enumeration changed forcing.");
        });
        check("partial duplicate nonfinite and overflowing forcing is refused", () =>
        {
            var motion = first.Plates.Select(p => new OriginMotion(p.Id, p.Vx, p.Vz)).ToArray();
            Refuse(() => first.ContinuePrehistory(scale, 0, motion.Skip(1).ToArray()));
            var duplicate = (OriginMotion[])motion.Clone(); duplicate[0] = duplicate[1];
            Refuse(() => first.ContinuePrehistory(scale, 0, duplicate));
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, 4001d })
            {
                var wrong = (OriginMotion[])motion.Clone(); wrong[0] = new(0, bad, 0);
                Refuse(() => first.ContinuePrehistory(scale, 0, wrong));
            }
        });
        check("entire-atlas resizing preserves the resumed result", () =>
        {
            foreach (long size in new long[] { 131072, 262144, 1000000 })
            {
                var other = first.ContinuePrehistory(new TectonicScalePlan(size, size), 2);
                Require(other.FinalMaterialChecksum == split.FinalMaterialChecksum, "Resizing cropped or regenerated the atlas.");
                Near(other.SampleElevationKm(new TectonicScalePlan(size, size), size * .375, size * .625), split.SampleElevationKm(scale, 375000, 625000), 1e-12);
            }
        });
        check("changed aspect invalid duration and excessive phase budgets are refused", () =>
        {
            Refuse(() => first.ContinuePrehistory(new TectonicScalePlan(1000000, 500000), 0));
            foreach (double t in new[] { -1d, 101d, double.NaN, double.PositiveInfinity }) Refuse(() => first.ContinuePrehistory(scale, t));
            Refuse(() => OceanPrehistory.Run(seed, scale, Settings(0), [new("a", 100), new("b", 100), new("c", 1)], assemblage));
            Refuse(() => OceanPrehistory.Run(seed, scale, Settings(1), [new("a", 1)], assemblage));
        });
        check("phase driver preserves material lineage and cumulative step counters", () =>
        {
            var run = OceanPrehistory.Run(seed, scale, Settings(0), [new("first", 1), new("second", 2)], assemblage);
            Require(run.FinalHistory.FinalMaterialChecksum == whole.FinalMaterialChecksum, "Driver reinitialized material.");
            Require(run.Phases[1].InitialMaterialChecksum == run.Phases[0].FinalMaterialChecksum, "Broken phase lineage.");
            Require(run.Phases[1].CumulativeSteps == 3, "Step counter reset.");
            if (output is not null)
            {
                string d = Path.Combine(output, "prehistory"); Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, "receipts.json"), JsonSerializer.Serialize(new { scope="CORE_PHASE_CONTINUITY_TEST_NOT_ACCEPTED_WORLD", run.Checksum,
                    run.Phases, finalMaterialChecksum=run.FinalHistory.FinalMaterialChecksum, uninterruptedMaterialChecksum=whole.FinalMaterialChecksum,
                    geographicAcceptance="NOT_EVALUATED", erosion="NOT_RUN" }, new JsonSerializerOptions { WriteIndented=true }));
            }
        });
        check("explicit birth records remain identified after continuation", () =>
        {
            var o = assemblage.OceanicKm; var births = o.Select(v => v > 0 ? -50d : 0).ToArray();
            var labels = o.Select(v => v > 0 ? 4 : -1).ToArray();
            var map = OceanBirthMap.Create(seed, scale, n, o, births, labels,
                [new(4,-50,-50,"SYNTHETIC_UNIFORM_IMPORT_CONTROL")], "CONTROL_NOT_EARTH");
            var a = MaterialBoundHistory.BeginPrehistory(seed, scale, Settings(0,0), assemblage, map).ContinuePrehistory(scale, 2);
            Require(a.InitialOceanBirthMapChecksum == map.Checksum, "Imported records lost.");
            var status = OceanPrehistory.Describe(a);
            Require(!status.HasUniformAgePriorWithoutBirthEvidence && status.InitialChronologyPolicy.StartsWith("SUPPLIED_INITIAL_BIRTH_RECORDS"), "Records relabelled as unknown.");
            Require(status.Status.Contains("NOT_GEOGRAPHIC_ACCEPTANCE"), "Numerical dates claimed geographic validity.");
        });
        check("legacy serialization does not acquire a null parent field", () =>
        {
            string text = JsonSerializer.Serialize(start);
            Require(!text.Contains("ContinuationParentChecksum") && !text.Contains("CanContinuePrehistory"), "Diagnostic retention changed default serialization.");
        });
        check("invalid schedule is rejected rather than partially advanced", () =>
        {
            Refuse(() => OceanPrehistory.Run(seed, scale, Settings(0), [new("valid", 1), new("bad", -1)], assemblage));
            Refuse(() => OceanPrehistory.Run(seed, scale, Settings(0), [], assemblage));
            Require(first.FinalMaterialChecksum == first.ContinuePrehistory(scale,0).FinalMaterialChecksum, "An unrelated failure mutated retained material.");
        });
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, double tolerance=1e-10)
    { Require(double.IsFinite(actual) && Math.Abs(actual-expected)<=tolerance, $"{actual:R} != {expected:R}"); }
    private static void Refuse(Action action)
    { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Expected refusal."); }
}
