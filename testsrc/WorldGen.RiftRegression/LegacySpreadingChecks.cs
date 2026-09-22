using ISRWorldGen.Core.Geology.Evolution;

// Preserved assertions from ocean-evidence 316ca3e / executed e1800d62.
// Only the 27 tests whose dependencies are the three imported library classes
// are included. Cooling, full history and spatial-index tests remain on their
// own experimental branches; this is NOT a claim to run their entire suites.
internal static class LegacySpreadingChecks
{
    internal static void Run(Action<string, Action> test)
    {
        void Check(string name, Action action) => test("legacy: " + name, action);
        {
var episode = new SpreadingEpisode(3, 0, 0, 0, 1000, 0, 0, 10, 0, -100, 0);
Check("birth inversion reconstructs a known parcel", () =>
{
    Require(SpreadingKinematics.TryResolveBirth(episode, 300, 200, out var w), "Missing known birth.");
    Near(w.BirthTimeMyr, -30); Near(w.AlongSegment, .2); Near(w.ReconstructedX, 300); Near(w.ReconstructedZ, 200);
});
Check("spreading rate rather than distance alone controls age", () =>
{
    Require(SpreadingKinematics.TryResolveBirth(episode with { MaterialVelocityX = 20 }, 300, 200, out var w), "Missing birth.");
    Near(w.BirthTimeMyr, -15);
});
Check("common velocity translation leaves birth time invariant", () =>
{
    var shifted = episode with { RidgeVelocityX = 6, RidgeVelocityZ = -2, MaterialVelocityX = 16, MaterialVelocityZ = -2 };
    Require(SpreadingKinematics.TryResolveBirth(shifted, 300, 200, out var w), "Missing birth."); Near(w.BirthTimeMyr, -30);
});
Check("moving ridge is accounted for", () =>
{
    Require(SpreadingKinematics.TryResolveBirth(episode with { RidgeVelocityX = 4 }, 300, 200, out var w), "Missing birth."); Near(w.BirthTimeMyr, -50);
});
Check("extinct ridge cannot create younger floor", () => Require(!SpreadingKinematics.TryResolveBirth(episode with { LastBirthTimeMyr = -40 }, 300, 200, out _), "Young birth outside event."));
Check("outside swept segment has no nearest-ridge fallback", () => Require(!SpreadingKinematics.TryResolveBirth(episode, 300, 1200, out _), "Outside parcel assigned an age."));
Check("tangential motion is not an ocean spreading episode", () => Refuse(() => SpreadingKinematics.TryResolveBirth(episode with { MaterialVelocityX = 0, MaterialVelocityZ = 10 }, 300, 200, out _)));
Check("segment reversal does not reverse the age", () =>
{
    Require(SpreadingKinematics.TryResolveBirth(episode with { Az = 1000, Bz = 0 }, 300, 200, out var w), "Missing reversed birth."); Near(w.BirthTimeMyr, -30); Near(w.AlongSegment, .8);
});

const int side = 64, seed = 73; int count = side * side;
var scale = new TectonicScalePlan(1_000_000, 1_000_000);
var assemblage = ContinentalAssemblage.Generate(seed, scale, side);
double[] o = assemblage.OceanicKm.ToArray();
double[] births = Enumerable.Range(0, count).Select(i => o[i] > 0 ? -(5d + i % side) : 0d).ToArray();
int[] labels = o.Select(q => q > 0 ? 3 : -1).ToArray();
OceanFormationEvent[] events = [new(3, -100, 0, "SYNTHETIC_INTEGRATION_CONTROL_NOT_EARTH")];
OceanBirthMap Map(double[] b, int[] ids, OceanFormationEvent[]? ev = null, string provenance = "SYNTHETIC_RASTER_FOR_API_TEST") =>
    OceanBirthMap.Create(seed, scale, side, o, b, ids, ev ?? events, provenance);

Check("chronology is immutable", () =>
{
    var b = (double[])births.Clone(); var ids = (int[])labels.Clone(); var map = Map(b, ids);
    int i = Array.FindIndex(o, q => q > 0); double old = map.BirthTimeMyr[i]; b[i] = -99; ids[i] = 999;
    Near(map.BirthTimeMyr[i], old); Require(map.SourceEventIds[i] == 3, "Source array escaped.");
});
Check("missing ocean birth is refused", () =>
{
    var ids = (int[])labels.Clone(); ids[Array.FindIndex(o, q => q > 0)] = -1; Refuse(() => Map(births, ids));
});
Check("future and out-of-event births refused", () =>
{
    int i = Array.FindIndex(o, q => q > 0);
    foreach (double t in new[] { 1d, -101d, double.NaN }) { var b = (double[])births.Clone(); b[i] = t; Refuse(() => Map(b, labels)); }
});
Check("duplicate events refused", () => Refuse(() => Map(births, labels, [events[0], events[0]])));
Check("provenance contributes to immutable checksum", () => Require(Map(births, labels).Checksum != Map(births, labels, provenance: "OTHER_EXPERIMENT").Checksum, "Provenance not bound."));

Check("whole analytic spreading basin yields a complete birth map", () =>
{
    SpreadingEpisode left = new(7, 500000, 0, 500000, 1000000, 0, 0, -2500, 0, -200, 0);
    SpreadingEpisode right = left with { MaterialVelocityX = 2500 };
    var ocean = Enumerable.Repeat(7d, count).ToArray();
    var map = OceanBirthMap.Reconstruct(seed, scale, side, ocean, new[] { left, right }, "ANALYTIC_BASIN_NOT_GENERATED_WORLD");
    var reversed = OceanBirthMap.Reconstruct(seed, scale, side, ocean, new[] { right, left }, "ANALYTIC_BASIN_NOT_GENERATED_WORLD");
    Require(map.Checksum == reversed.Checksum, "Episode order changed the semantic chronology.");
    for (int i = 0; i < count; i++) Near(map.AgeMyr[i], Math.Abs((i % side + .5) * 1000000 / side - 500000) / 2500, 1e-10);
});
Check("incomplete spreading history never fills unknown floor", () =>
{
    SpreadingEpisode onlyRight = new(7, 500000, 0, 500000, 1000000, 0, 0, 2500, 0, -200, 0);
    Refuse(() => OceanBirthMap.Reconstruct(seed, scale, side, Enumerable.Repeat(7d, count).ToArray(), new[] { onlyRight }, "INCOMPLETE_TEST"));
});
Check("overlapping incompatible birth events are refused", () =>
{
    SpreadingEpisode a = new(7, 0, 0, 0, 1000000, 0, 0, 5000, 0, -200, 0);
    Refuse(() => OceanBirthMap.Reconstruct(seed, scale, side, Enumerable.Repeat(7d, count).ToArray(), new[] { a, a with { SourceEventId = 8 } }, "CONFLICT_TEST"));
});

        }
        {
        SpreadingPhase old = new(7, -100, -40, 0, 0, 0, 1000, 0, 0, 10, 0);
        SpreadingPhase passive = new(-1, -40, 0, 0, 0, 0, 1000, 0, 0, 20, 0, false);
        var timeline = new SpreadingTimeline("extinct", [old, passive]);
        Check("extinct ridge birth includes all later displacement", () =>
        {
            Require(timeline.TryResolveBirth(1000, 200, out var w), "Missing old floor");
            Near(w.BirthTimeMyr, -60); Near(w.ReconstructedX, 1000);
            Require(Math.Abs(w.BirthTimeMyr + 1000d / 20) > 1, "Current distance/rate wrongly used");
        });
        Check("phase order is canonical rather than caller dependent", () =>
        {
            var t = new SpreadingTimeline("extinct", [passive, old]);
            Require(t.TryResolveBirth(1000, 200, out var w), "Missing floor"); Near(w.BirthTimeMyr, -60);
        });
        Check("timeline includes later tangential transport", () =>
        {
            var t = new SpreadingTimeline("sheared", [old, passive with { MaterialVelocityZ = 3 }]);
            Require(t.TryResolveBirth(1000, 320, out var w), "Missing sheared floor");
            Near(w.BirthTimeMyr, -60); Near(w.AlongSegment, .2);
        });
        Check("timeline common-frame translation preserves birth", () =>
        {
            const double ux = 7, uz = -3;
            var shifted = timeline.Phases.Select(p => p with {
                Ax = p.Ax + ux * p.StartTimeMyr, Az = p.Az + uz * p.StartTimeMyr,
                Bx = p.Bx + ux * p.StartTimeMyr, Bz = p.Bz + uz * p.StartTimeMyr,
                RidgeVelocityX = p.RidgeVelocityX + ux, RidgeVelocityZ = p.RidgeVelocityZ + uz,
                MaterialVelocityX = p.MaterialVelocityX + ux, MaterialVelocityZ = p.MaterialVelocityZ + uz }).ToArray();
            Require(new SpreadingTimeline("frame", shifted).TryResolveBirth(1000, 200, out var w), "Frame changed coverage"); Near(w.BirthTimeMyr, -60);
        });
        Check("gaps overlaps and omitted recent motion are refused", () =>
        {
            Refuse(() => new SpreadingTimeline("gap", [old, passive with { StartTimeMyr = -39 }]));
            Refuse(() => new SpreadingTimeline("overlap", [old, passive with { StartTimeMyr = -41 }]));
            Refuse(() => new SpreadingTimeline("unfinished", [old]));
        });
        Check("extinction endpoint remains dated without a newer birth event", () =>
        {
            Require(timeline.TryResolveBirth(800, 200, out var w), "Missing extinction endpoint");
            Near(w.BirthTimeMyr, -40); Require(w.SourceEventId == 7, "Invented recent event");
        });
        Check("dormant interval cannot manufacture ocean", () =>
        {
            Require(!timeline.TryResolveBirth(200, 200, out _), "Created material after extinction");
            Refuse(() => new SpreadingTimeline("false-event", [old, passive with { SourceEventId = 8 }]));
        });
        Check("timeline retains immutable inputs", () =>
        {
            var input = new[] { old, passive }; var t = new SpreadingTimeline("immutable", input);
            input[0] = old with { MaterialVelocityX = 100 };
            Require(t.TryResolveBirth(1000, 200, out var w), "External mutation changed coverage"); Near(w.BirthTimeMyr, -60);
        });
        var scale = new TectonicScalePlan(1_000_000, 1_000_000);
        SpreadingTimeline Branch(int sign) => new("branch-" + sign, [
            new(10, -175, -25, 500000, 0, 500000, 1000000, 0, 0, sign * 2500, 0),
            new(11, -25, 0, 500000, 0, 500000, 1000000, 0, 0, sign * 5000, 0)]);
        var left = Branch(-1); var right = Branch(1);
        Check("shared phase boundary belongs to the younger event", () =>
        {
            Require(right.TryResolveBirth(625000, 500000, out var w), "Unresolved phase boundary");
            Near(w.BirthTimeMyr, -25); Require(w.SourceEventId == 11, "Temporal boundary selected old event");
        });
        Check("changing spreading rate does not overwrite inherited dates", () =>
        {
            Require(right.TryResolveBirth(750000, 500000, out var w), "Missing older phase"); Near(w.BirthTimeMyr, -75);
        });
        Check("partial and conflicting atlas timelines cannot become complete maps", () =>
        {
            var ocean = Enumerable.Repeat(7d, 32*32).ToArray();
            Refuse(() => SpreadingTimeline.Reconstruct(73,scale,32,ocean,[right],"PARTIAL"));
            var other = new SpreadingTimeline("other", right.Phases.Select(p => p with { SourceEventId = p.SourceEventId+10 }).ToArray());
            Refuse(() => SpreadingTimeline.Reconstruct(73,scale,32,ocean,[right,left,other],"CONFLICT"));
        });

        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, double tolerance = 1e-9)
    { Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{actual:R} != {expected:R}"); }
    private static void Refuse(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected an explicit argument refusal."); }
}
