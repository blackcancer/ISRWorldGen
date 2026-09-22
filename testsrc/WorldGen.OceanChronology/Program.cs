using ISRWorldGen.Core.Geology.Evolution;
using System.Text.Json;

string? output = args.Length == 1 ? Path.GetFullPath(args[0]) : null;
if (args.Length > 1) throw new ArgumentException("Usage: OceanChronology [new-output-directory]");
if (output is not null)
{
    if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; do not overwrite evidence.");
    Directory.CreateDirectory(output);
}
var passed = new List<string>(); var failed = new List<string>();
void Check(string name, Action action)
{
    try { action(); passed.Add(name); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed.Add(name + ": " + ex); Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
void Near(double actual, double expected, double tolerance = 1e-9)
{
    Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{actual:R} != {expected:R}");
}
void Refuse(Action action)
{
    try { action(); } catch (ArgumentException) { return; }
    throw new InvalidOperationException("Expected an explicit argument refusal.");
}

Check("hot material has zero cooling anomaly", () => Near(OceanCoolingColumn.Evaluate(0).WaterLoadedSubsidenceMetres, 0, 0));
Check("old finite plate approaches finite thickness", () => Near(OceanCoolingColumn.Evaluate(10000).IntegratedTemperatureDeficitMetres, 62500, 1e-8));
Check("cooling is monotonic and bounded", () =>
{
    double previous = 0;
    for (int i = 0; i <= 500; i++)
    {
        var q = OceanCoolingColumn.Evaluate(i * .4);
        Require(q.IntegratedTemperatureDeficitMetres >= previous && q.IntegratedTemperatureDeficitMetres <= 62500, "Nonmonotonic/unbounded cooling.");
        previous = q.IntegratedTemperatureDeficitMetres;
    }
});
Check("young plate converges to the half-space limit", () =>
{
    double expected = 2 * Math.Sqrt(1e-6 * .001 * OceanCoolingColumn.SecondsPerMyr / Math.PI);
    Near(OceanCoolingColumn.Evaluate(.001).IntegratedTemperatureDeficitMetres, expected, 1e-8);
});
Check("age and diffusivity enter the same Fourier number", () =>
    Near(OceanCoolingColumn.Evaluate(20).WaterLoadedSubsidenceMetres,
         OceanCoolingColumn.Evaluate(10, new(diffusivityM2PerSecond: 2e-6)).WaterLoadedSubsidenceMetres));
Check("water loading changes displacement not density excess", () =>
{
    var a = OceanCoolingColumn.Evaluate(50); var b = OceanCoolingColumn.Evaluate(50, new(waterDensityKgM3: 0));
    Near(a.DensityExcessKgPerSquareMetre, b.DensityExcessKgPerSquareMetre);
    Near(a.WaterLoadedSubsidenceMetres / b.WaterLoadedSubsidenceMetres, 3300d / 2270d, 1e-12);
});
Check("invalid ages and thermal parameters refused", () =>
{
    foreach (double age in new[] { -1d, double.NaN, double.PositiveInfinity }) Refuse(() => OceanCoolingColumn.Evaluate(age));
    Refuse(() => new OceanCoolingParameters(thicknessMetres: 0));
    Refuse(() => new OceanCoolingParameters(waterDensityKgM3: 3300));
});
Check("thermal response of a mixture is not response at mean age", () =>
{
    double mix = OceanCoolingColumn.MixedDensityExcess(new[] { 5d, 95d }, new[] { 1d, 1d });
    Near(mix, (OceanCoolingColumn.Evaluate(5).DensityExcessKgPerSquareMetre + OceanCoolingColumn.Evaluate(95).DensityExcessKgPerSquareMetre) / 2, 1e-8);
    Require(OceanCoolingColumn.Evaluate(50).DensityExcessKgPerSquareMetre > mix, "Missing nonlinear age-mixture effect.");
});
Check("empty thermal mixture cannot be assigned a false age", () => Refuse(() => OceanCoolingColumn.MixedDensityExcess(new[] { 50d }, new[] { 0d })));

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

var settings = new TectonicEvolutionSettings(side: side, duration: 2, speedReferenceUnitsPerTime: 0, lowerCrustMobility: 0);
Check("full material history ages explicit birth records without motion", () =>
{
    var h = MaterialBoundHistory.GenerateWithOceanBirthMap(seed, scale, settings, assemblage, Map(births, labels));
    Require(h.InitialOceanBirthMapChecksum == Map(births, labels).Checksum, "History omitted initial chronology identity.");
    for (int i = 0; i < count; i++)
    {
        Near(h.Final.ContinentalKm[i], h.Initial.ContinentalKm[i]); Near(h.Final.OceanicKm[i], h.Initial.OceanicKm[i]);
        if (o[i] > 0) Near(h.Final.OceanAge[i], -births[i] + 2, 1e-10);
    }
});
Check("explicit uniform control retains old numerical results", () =>
{
    var uniform = o.Select(q => q > 0 ? -50d : 0d).ToArray();
    var old = MaterialBoundHistory.GenerateWithAssemblage(seed, scale, settings, assemblage);
    var now = MaterialBoundHistory.GenerateWithOceanBirthMap(seed, scale, settings, assemblage, Map(uniform, labels));
    for (int i = 0; i < count; i++) Near(now.Final.ElevationKm[i], old.Final.ElevationKm[i], 1e-10);
    Require(old.InitialOceanBirthMapChecksum is null, "Legacy entry silently opted into new chronology.");
});
Check("wrong atlas material identity is refused", () =>
{
    var alteredOcean = (double[])o.Clone(); alteredOcean[Array.FindIndex(o, q => q > 0)] *= .9;
    var map = OceanBirthMap.Create(seed, scale, side, alteredOcean, births, labels, events, "WRONG_CARRIER_TEST");
    Refuse(() => MaterialBoundHistory.GenerateWithOceanBirthMap(seed, scale, settings, assemblage, map));
});
Check("resizing keeps the whole birth map, not a clipped ocean", () =>
{
    var map = Map(births, labels);
    var h = MaterialBoundHistory.GenerateWithOceanBirthMap(seed, scale, settings, assemblage, map);
    foreach (long size in new long[] { 131072, 262144, 1000000 })
    {
        var target = new TectonicScalePlan(size, size);
        Near(h.SampleElevationKm(target, size * .375, size * .625), h.SampleElevationKm(scale, 375000, 625000), 1e-10);
    }
});
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
QualificationChecks.Run(Check, output);
PrehistoryChecks.Run(Check, output);
string report = JsonSerializer.Serialize(new { status = failed.Count == 0 ? "PASS" : "FAIL", passed, failed,
    geographicAcceptance = "NOT_EVALUATED", erosion = "NOT_RUN", nativeGame = "NOT_RUN" }, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(report);
if (output is not null)
{
    File.WriteAllText(Path.Combine(output, "checks.json"), report);
    if (failed.Count == 0) File.WriteAllText(Path.Combine(output, "COMPLETE.json"), "{\"status\":\"NUMERICAL_CHECKS_ONLY\",\"geographicAcceptance\":\"NOT_EVALUATED\"}");
}
return failed.Count == 0 ? 0 : 1;
