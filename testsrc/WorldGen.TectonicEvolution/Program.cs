using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

if (args.Length is < 1 or > 3 || (args.Length >= 2 && (!int.TryParse(args[1], out int parsed) || parsed is < 64 or > 512 || (parsed & (parsed - 1)) != 0))
    || (args.Length == 3 && args[2] is not ("material" or "reference")))
    throw new ArgumentException("Usage: WorldGen.TectonicEvolution <new-directory> [side=256] [material|reference]");
int side = args.Length >= 2 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 256;
bool material = args.Length < 3 || args[2] == "material";
string root = Path.GetFullPath(args[0]);
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Evidence directory exists; never overwrite an earlier campaign.");
string[] checks = TectonicChecks.Run().Concat(PolarityRegressionChecks.Run()).Concat(MaterialDomainChecks.Run()).ToArray();
Directory.CreateDirectory(root);
var json = new JsonSerializerOptions { WriteIndented = true };
var reports = new List<object>();
foreach (int seed in new[] { -437287116, 73, 20260906 })
{
    var reference = new TectonicScalePlan(1_000_000, 1_000_000);
    var settings = new TectonicEvolutionSettings(side: side, advectPlateDomains: material);
    TectonicHistory history = TectonicHistory.Generate(seed, reference, settings);
    int components = CountLargeLandComponents(history.Final.ElevationKm, side);
    string seedRoot = Path.Combine(root, "seed-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    Directory.CreateDirectory(seedRoot);
    var fields = new Dictionary<string, object>();
    WriteField("initial-height", history.Initial.ElevationKm.Select(v => 168 + 12 * v).ToArray(), "solid Y blocks, fixed 12 blocks/model-km; no water");
    WriteField("height", history.Final.ElevationKm.Select(v => 168 + 12 * v).ToArray(), "solid Y blocks, fixed 12 blocks/model-km; no water");
    WriteField("continental-thickness", history.Final.ContinentalKm, "km equivalent continental crust");
    WriteField("oceanic-thickness", history.Final.OceanicKm, "km equivalent oceanic crust");
    WriteField("ocean-age", history.Final.OceanAge, "mean model-time age of advected oceanic volume; 0 where absent");
    WriteField("new-ocean-fraction", history.Final.OceanicKm.Select((v, i) => v > 0 ? 1 - history.Final.InheritedOceanicKm[i] / v : 0).ToArray(), "fraction of current oceanic volume formed during this history");
    WriteField("compression", history.Final.AccumulatedCompression, "accumulated negative divergence; Eulerian diagnostic");
    WriteField("extension", history.Final.AccumulatedExtension, "accumulated positive divergence; Eulerian diagnostic");
    WriteField("shear", history.Final.AccumulatedShear, "accumulated shear strain; Eulerian diagnostic");
    WriteField("plates", history.Final.PlateIds.Select(v => (double)v).ToArray(), material ? "advected in-plane carrier identifier; not surviving crust provenance" : "moving Voronoi reference-domain identifier");
    WriteField("plate-confidence", history.Final.PlateConfidence, "dominant in-plane carrier fraction; not certainty of physical reconstruction");
    WriteField("carrier-density", history.Final.CarrierDensity, material ? "conservative reference-area measure per current area" : "reference mode sentinel = 1; no carrier evolved");
    var scaleChecks = new List<object>();
    foreach (long size in new long[] { 131072, 262144, 1_000_000 })
    {
        var scale = new TectonicScalePlan(size, size); double maximumDifference = 0;
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            double sample = history.SampleElevationKm(scale, (x + .5) * size / side, (z + .5) * size / side);
            maximumDifference = Math.Max(maximumDifference, Math.Abs(sample - history.Final.ElevationKm[z * side + x]));
        }
        if (maximumDifference > 1e-10) throw new InvalidOperationException("Resizing lost full-atlas correspondence.");
        scaleChecks.Add(new { size, scale.Mode, scale.ReferenceWidth, scale.ReferenceLength, scale.BlocksPerReferenceUnit,
            sampleStepBlocks = size / (double)side, maximumDifferenceKm = maximumDifference, largeLandComponents = components,
            sameCompleteAtlas = true, crop = false });
    }
    double[] solid = history.Final.ElevationKm.Select(v => 168 + 12 * v).ToArray();
    if (solid.Any(v => !double.IsFinite(v) || v < 0 || v > 383)) throw new InvalidOperationException("Explicit vertical conversion out of budget; refuse, never clamp.");
    var summary = new
    {
        seed, algorithm = TectonicHistory.AlgorithmId, commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "UNVERIFIED_WORKTREE",
        historyChecksum = history.Checksum, initialChecksum = history.Initial.Checksum, finalChecksum = history.Final.Checksum,
        coreAssemblySha256 = Hash(File.ReadAllBytes(typeof(TectonicHistory).Assembly.Location)),
        scope = "EXPERIMENTAL_KINEMATIC_MATERIAL_HISTORY_NOT_FORCE_BALANCED", geographicAcceptance = "NOT_ACCEPTED_PENDING_REFERENCE_REVIEW",
        nativeGame = "NOT_RUN", erosion = "NOT_RUN_GATED_ON_RELIEF_REVIEW", independentCodeReview = "NOT_RUN",
        boundary = "PERIODIC_PLANAR_PREPARATORY_ATLAS; does not imply native player wrapping",
        worldWidthBlocks = 1_000_000, worldLengthBlocks = 1_000_000, history.ReferenceWidth, history.ReferenceLength,
        referenceKmPerUnit = TectonicHistory.ReferenceKmPerUnit, worldHeightBlocks = 384, seaLevelReferenceBlocks = 168,
        blocksPerModelKm = 12, width = side, height = side, settings, scaleChecks, fields,
        history.InitialCarrierInventory, history.FinalCarrierInventory,
        waterSurfacePresent = false, seabedMasked = false, perImageAutoContrast = false,
        steps = history.Ledger.Count - 1, initial = history.Ledger[0], final = history.Ledger[^1],
        solidMin = solid.Min(), solidMax = solid.Max(), largeLandComponents = components,
        landFraction = history.Final.ElevationKm.Count(v => v >= 0) / (double)(side * side),
        solverNotes = new[] { "First-order donor-cell numerical diffusion remains measurable.",
            material ? "Plate coordinates follow the crust face velocities; prescribed plate speeds are not a force balance." : "Moving Voronoi reference; material assignment is not advected.",
            "Subduction recycles volume on selected lower sides, but flexural trench/arc mechanics are not implemented.",
            "No tectonic phase is hidden in a coloured rendering. No Earth data is copied into the generated height field." }
    };
    File.WriteAllText(Path.Combine(seedRoot, "manifest.json"), JsonSerializer.Serialize(summary, json));
    File.WriteAllText(Path.Combine(seedRoot, "history-ledger.json"), JsonSerializer.Serialize(history.Ledger, json));
    reports.Add(summary);
    Console.WriteLine($"HISTORY seed={seed} side={side} steps={history.Ledger.Count - 1} height=[{solid.Min():R},{solid.Max():R}] largeLandComponents={components}; geography=NOT_ACCEPTED");
    void WriteField(string name, IReadOnlyList<double> values, string units)
    {
        byte[] bytes = new byte[values.Count * 8];
        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) throw new ArithmeticException("Nonfinite diagnostic field.");
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * 8, 8), values[i]);
        }
        File.WriteAllBytes(Path.Combine(seedRoot, name + ".f64le"), bytes);
        fields.Add(name, new { path = name + ".f64le", sha256 = Hash(bytes), units, minimum = values.Min(), maximum = values.Max(), encoding = "float64 little-endian row-major X right Z down" });
    }
}
File.WriteAllText(Path.Combine(root, "verification.json"), JsonSerializer.Serialize(new
{
    status = "PASS_NUMERICAL_ONLY", algorithm = TectonicHistory.AlgorithmId, checksPassed = checks.Length, checks,
    context = "complete reference atlas at 1000000 units mapped without cropping to 131072, 262144 and 1000000 blocks",
    geographicAcceptance = "NOT_ACCEPTED", erosion = "NOT_RUN", nativeGame = "NOT_RUN", reports
}, json));
Console.WriteLine($"TECTONIC_CHECKS={checks.Length}; three generated histories; no geographic acceptance implied.");

static int CountLargeLandComponents(IReadOnlyList<double> values, int side)
{
    var seen = new bool[values.Count]; int large = 0; var queue = new Queue<int>();
    for (int i = 0; i < values.Count; i++)
    {
        if (seen[i] || values[i] < 0) continue;
        seen[i] = true; queue.Enqueue(i); int size = 0;
        while (queue.TryDequeue(out int p))
        {
            size++; int x = p % side, z = p / side;
            foreach (int next in new[] { z * side + (x + 1) % side, z * side + (x + side - 1) % side,
                ((z + 1) % side) * side + x, ((z + side - 1) % side) * side + x })
                if (!seen[next] && values[next] >= 0) { seen[next] = true; queue.Enqueue(next); }
        }
        if (size >= Math.Max(4, values.Count / 100)) large++;
    }
    return large;
}
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
