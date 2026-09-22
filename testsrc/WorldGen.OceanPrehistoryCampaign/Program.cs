using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

if (args.Length != 2) throw new ArgumentException("Usage: OceanPrehistoryCampaign NEW_OUTPUT_DIRECTORY SEED");
string root = Path.GetFullPath(args[0]);
int seed = int.Parse(args[1], CultureInfo.InvariantCulture);
if (seed is not (-437287116 or 20260906 or 73)) throw new ArgumentException("Use the three frozen comparison seeds.");
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Evidence path exists; never overwrite a campaign.");
Directory.CreateDirectory(root);
var json = new JsonSerializerOptions { WriteIndented = true };
const int side = 512;
var scale = new TectonicScalePlan(1_000_000, 1_000_000);
var template = new TectonicEvolutionSettings(side: side, duration: 0);
var stages = new List<object>();
string commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "UNCOMMITTED";
WriteJson(Path.Combine(root, "INCOMPLETE.json"), new { seed, commit, status = "INCOMPLETE" });
try
{
    // Match the earlier assemblage campaign's starting continental volume.
    // The only experiment is extending the SAME prescribed-motion history.
    var old = TectonicHistory.Generate(seed, scale, template);
    double budget = CrustTransport.Sum(old.Initial.ContinentalKm) / (side * side);
    var assemblage = ContinentalAssemblage.Generate(seed, scale, side, budget);
    var current = MaterialBoundHistory.BeginPrehistory(seed, scale, template, assemblage);
    double c0 = current.Ledger[0].ContinentalVolume, o0 = current.Ledger[0].OceanicVolume;
    double created = 0, recycled = 0;
    ExportStage(current, 0, created, recycled);
    for (int phase = 1; phase <= 2; phase++)
    {
        string previous = current.FinalMaterialChecksum;
        current = current.ContinuePrehistory(scale, 36);
        if (current.InitialMaterialChecksum != previous) throw new ArithmeticException("Phase reinitialized material.");
        var end = current.Ledger[^1];
        created += end.CreatedOceanicVolume; recycled += end.RecycledOceanicVolume;
        CrustTransport.RequireBalance(c0, end.ContinentalVolume, "campaign continental inventory");
        CrustTransport.RequireBalance(o0 + created - recycled, end.OceanicVolume, "campaign ocean inventory");
        ExportStage(current, phase, created, recycled);
    }
    WriteJson(Path.Combine(root, "COMPLETE.json"), new { seed, commit, side, phases = 2,
        status = "NUMERIC_CONTINUATION_ONLY", stages, geographicAcceptance = "NOT_EVALUATED", erosion = "NOT_RUN" });
    File.Delete(Path.Combine(root, "INCOMPLETE.json"));
    return 0;
}
catch (Exception ex)
{
    WriteJson(Path.Combine(root, "FAILED.json"), new { seed, commit, error = ex.ToString(), stages,
        status = "FAIL", geographicAcceptance = "NOT_EVALUATED", erosion = "NOT_RUN" });
    Console.Error.WriteLine(ex);
    return 1;
}

void ExportStage(MaterialBoundHistory history, int phase, double totalCreated, double totalRecycled)
{
    var snapshot = history.Final;
    string name = "stage-" + ((int)snapshot.Time).ToString("D3", CultureInfo.InvariantCulture);
    string directory = Path.Combine(root, name);
    if (Directory.Exists(directory)) throw new IOException("Stage already exported.");
    Directory.CreateDirectory(directory);
    var fields = new Dictionary<string, object>();
    Save("height", snapshot.ElevationKm.Select(v => 168 + 12 * v).ToArray(), "solid Y (blocks)");
    Save("elevation-model", snapshot.ElevationKm, "signed km model relative to marine datum");
    Save("continental-thickness", snapshot.ContinentalKm, "equivalent km of continental crust");
    Save("oceanic-thickness", snapshot.OceanicKm, "equivalent km of oceanic crust");
    Save("ocean-age", snapshot.OceanAge, "model Myr; mean, not a recovered event distribution");
    Save("inherited-oceanic-thickness", snapshot.InheritedOceanicKm, "equivalent km of inherited oceanic crust");
    Save("compression", snapshot.AccumulatedCompression, "cumulative dimensionless strain");
    Save("extension", snapshot.AccumulatedExtension, "cumulative dimensionless strain");
    var coverage = OceanPrehistory.Describe(history);
    foreach (long size in new long[] { 131072, 262144, 1000000 })
    {
        double value = history.SampleElevationKm(new TectonicScalePlan(size, size), size * .375, size * .625);
        double reference = history.SampleElevationKm(scale, 375000, 625000);
        if (Math.Abs(value - reference) > 1e-10) throw new ArithmeticException("Resizing cropped or changed the atlas.");
    }
    var summary = new { phase, time = snapshot.Time, history.CompletedPrehistorySteps,
        history.InitialMaterialChecksum, history.FinalMaterialChecksum, history.InitialAssemblageChecksum,
        history.ContinuationParentChecksum, history.Checksum, coverage, totalCreated, totalRecycled,
        minimumSolidKm = snapshot.ElevationKm.Min(), maximumSolidKm = snapshot.ElevationKm.Max() };
    WriteJson(Path.Combine(directory, "summary.json"), summary);
    WriteJson(Path.Combine(directory, "ledger.json"), history.Ledger);
    WriteJson(Path.Combine(directory, "manifest.json"), new {
        scope = "FULL_ATLAS_FIXED_MOTION_CONTINUATION_DIAGNOSTIC_NOT_RESOLVED_PREHISTORY", seed, commit,
        mode = "fixed-motion-continued-" + ((int)snapshot.Time), width = side, height = side,
        worldWidthBlocks = 1000000, worldLengthBlocks = 1000000,
        ReferenceWidth = history.ReferenceWidth, ReferenceLength = history.ReferenceLength,
        referenceKmPerUnit = MaterialBoundHistory.ReferenceKmPerUnit,
        blocksPerModelKm = 12, seaLevelReferenceBlocks = 168,
        boundary = "PERIODIC_PLANAR_PREPARATORY_ATLAS", timeMyr = snapshot.Time,
        materialSide = side, mechanicalSide = (int?)null,
        mechanics = "PRESCRIBED_MATERIAL_MOTION_NOT_RHEOLOGY_OR_FORCE_SOLVER",
        algorithm = MaterialBoundHistory.ContinuationAlgorithmId, settings = history.Settings,
        runtime = RuntimeInformation.FrameworkDescription, operatingSystem = RuntimeInformation.OSDescription,
        fields, waterSurfacePresent = false, seabedMasked = false,
        initialOceanPolicy = "UNIFORM_50_MODEL_MYR_PRIOR_TRACKED_NOT_REPLACED",
        geographicAcceptance = "NOT_EVALUATED", erosion = "NOT_RUN", nativeGame = "NOT_RUN"
    });
    stages.Add(summary);
    WriteJson(Path.Combine(directory, "COMPLETE.json"), new { status = "EXACT_FIELDS_WRITTEN_NOT_GEOGRAPHIC_ACCEPTANCE" });
    Console.WriteLine($"STAGE seed={seed} time={snapshot.Time:R} inherited={coverage.InheritedCarrierFraction:R} maxKm={snapshot.ElevationKm.Max():R}");
    void Save(string field, IReadOnlyList<double> values, string units)
    {
        if (values.Count != side * side || values.Any(v => !double.IsFinite(v))) throw new ArithmeticException("Invalid field " + field);
        byte[] raw = new byte[values.Count * 8];
        for (int i = 0; i < values.Count; i++) BinaryPrimitives.WriteDoubleLittleEndian(raw.AsSpan(i * 8, 8), values[i]);
        string path = field + ".f64le";
        using (var file = new FileStream(Path.Combine(directory, path), FileMode.CreateNew)) file.Write(raw);
        fields.Add(field, new { path, sha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
            encoding = "float64 little-endian row-major X right Z down", units });
    }
}
void WriteJson(string path, object value)
{
    using var stream = new FileStream(path, FileMode.CreateNew);
    JsonSerializer.Serialize(stream, value, json);
}
