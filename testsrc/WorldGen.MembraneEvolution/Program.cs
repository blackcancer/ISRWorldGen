using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

if (args.Length != 3 || !int.TryParse(args[1], out int side) || side is < 64 or > 512 || (side & (side - 1)) != 0 || !int.TryParse(args[2], out int seed))
    throw new ArgumentException("Usage: WorldGen.MembraneEvolution <new-output-directory> <side64..512> <seed>");
string root = Path.GetFullPath(args[0]);
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Evidence directory exists; never overwrite.");
string[] checks = TectonicChecks.Run().Concat(PolarityRegressionChecks.Run()).Concat(MaterialCohortChecks.Run())
    .Concat(OceanCarrierChecks.Run()).Concat(AssemblageChecks.Run()).Concat(MembraneChecks.Run()).ToArray();
Directory.CreateDirectory(root);
var json = new JsonSerializerOptions { WriteIndented = true };
var reference = new TectonicScalePlan(1_000_000, 1_000_000);
var settings = new TectonicEvolutionSettings(side: side);
var legacy = MaterialBoundHistory.Generate(seed, reference, new TectonicEvolutionSettings(side: side, duration: 0));
double budget = CrustTransport.Sum(legacy.Initial.ContinentalKm) / (side * side);
var assemblage = ContinentalAssemblage.Generate(seed, reference, side, budget);
var baseline = MaterialBoundHistory.GenerateWithAssemblage(seed, reference, settings, assemblage);
Console.WriteLine($"BASELINE COMPLETE seed={seed}; starting material-dependent membrane solve");
var history = MaterialBoundHistory.GenerateWithMembrane(seed, reference, settings, assemblage);
if (history.InitialMaterialChecksum != baseline.InitialMaterialChecksum || history.Initial.Checksum != baseline.Initial.Checksum)
    throw new InvalidOperationException("Mechanical comparison changed the initial materials.");
var mechanical = history.FinalMembrane ?? throw new InvalidOperationException("Missing solved mechanics.");
string seedRoot = Path.Combine(root, "seed-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
Directory.CreateDirectory(seedRoot);
var fields = new Dictionary<string, object>();
Save(fields, seedRoot, "initial-height", history.Initial.ElevationKm.Select(Y).ToArray(), "solid Y, fixed conversion, no water");
Save(fields, seedRoot, "baseline-height", baseline.Final.ElevationKm.Select(Y).ToArray(), "unchanged kinematic baseline; SAME initial material and duration");
Save(fields, seedRoot, "height", history.Final.ElevationKm.Select(Y).ToArray(), "solid Y, fixed conversion, seabed unmasked");
Save(fields, seedRoot, "continental-thickness", history.Final.ContinentalKm, "equivalent crust km");
Save(fields, seedRoot, "oceanic-thickness", history.Final.OceanicKm, "equivalent crust km");
Save(fields, seedRoot, "ocean-age", history.Final.OceanAge, "mean advected ocean age; zero where absent");
Save(fields, seedRoot, "compression", history.Final.AccumulatedCompression, "Eulerian accumulated compression");
Save(fields, seedRoot, "extension", history.Final.AccumulatedExtension, "Eulerian accumulated extension");
Save(fields, seedRoot, "shear", history.Final.AccumulatedShear, "Eulerian accumulated shear");
Save(fields, seedRoot, "plates", history.Final.PlateIds.Select(v => (double)v).ToArray(), "dominant transported origin, not new Voronoi partition");
Save(fields, seedRoot, "owner-fraction", history.FinalOwnerFraction, "local dominant material fraction");
var scaleChecks = new List<object>();
foreach (long size in new[] { 131072L, 262144L, 1000000L })
{
    var scale = new TectonicScalePlan(size, size); double error = 0;
    for (int row = 0; row < side; row++) for (int col = 0; col < side; col++)
        error = Math.Max(error, Math.Abs(history.SampleElevationKm(scale, (col + .5) * size / side, (row + .5) * size / side)
            - history.Final.ElevationKm[row * side + col]));
    if (error > 1e-10) throw new ArithmeticException("Resizing did not preserve the complete atlas.");
    scaleChecks.Add(new { size, sameCompleteAtlas = true, crop = false, maxErrorKm = error, sampleStepBlocks = (double)size / side });
}
double[] solid = history.Final.ElevationKm.Select(Y).ToArray();
if (solid.Any(v => !double.IsFinite(v) || v < 0 || v > 383)) throw new ArithmeticException("Height out of explicit budget, no clamp.");
string mechanicalRoot = Path.Combine(seedRoot, "mechanics"); Directory.CreateDirectory(mechanicalRoot);
var mechanicalFields = new Dictionary<string, object>();
Save(mechanicalFields, mechanicalRoot, "resistance", mechanical.Resistance, "dimensionless depth-integrated viscosity prior, NOT density");
Save(mechanicalFields, mechanicalRoot, "east-velocity", mechanical.Solution.East, "reference units/model time; resolved east faces");
Save(mechanicalFields, mechanicalRoot, "south-velocity", mechanical.Solution.South, "reference units/model time; resolved south faces");
Save(mechanicalFields, mechanicalRoot, "divergence", mechanical.Solution.Divergence, "1/model time; resolved cells");
Save(mechanicalFields, mechanicalRoot, "shear-rate", mechanical.Solution.Shear, "1/model time; resolved vertices");
File.WriteAllText(Path.Combine(mechanicalRoot, "manifest.json"), JsonSerializer.Serialize(new {
    width = mechanical.SolveSide, height = mechanical.SolveSide, sampleStepBlocks = 1_000_000d / mechanical.SolveSide,
    algorithm = LithosphereMembrane.AlgorithmId, fields = mechanicalFields, residual = mechanical.Solution.RelativeForceResidual,
    normalization = "equation divided by basal drag coefficient; NOT SI-calibrated stresses"
}, json));
var summary = new {
    seed, commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "UNVERIFIED_WORKTREE",
    algorithm = "material-membrane-history-v1/" + MaterialBoundHistory.AlgorithmId,
    baselineChecksum = baseline.Checksum, historyChecksum = history.Checksum,
    coreAssemblySha256 = Hash(File.ReadAllBytes(typeof(MaterialBoundHistory).Assembly.Location)),
    scope = "REDUCED_VISCOUS_MEMBRANE_WITH_PRESCRIBED_BASAL_DRIVE_NOT_FULL_PLATE_DYNAMICS",
    initialAssemblageChecksum = assemblage.Checksum, provinces = assemblage.Provinces,
    width = side, height = side, settings, fields, scaleChecks,
    worldWidthBlocks = 1000000, worldLengthBlocks = 1000000,
    history.ReferenceWidth, history.ReferenceLength, referenceKmPerUnit = MaterialBoundHistory.ReferenceKmPerUnit,
    worldHeightBlocks = 384, seaLevelReferenceBlocks = 168, blocksPerModelKm = 12,
    waterSurfacePresent = false, seabedMasked = false, perImageAutoContrast = false,
    boundary = "PERIODIC_PLANAR_PREPARATORY_ATLAS; no implication of player wrapping",
    history.InitialContinentalByOrigin, history.FinalContinentalByOrigin, history.UnresolvedInterfaceFaces,
    history.InitialMaterialChecksum, history.FinalMaterialChecksum,
    initial = history.Ledger[0], final = history.Ledger[^1], steps = history.Ledger.Count - 1,
    solidMin = solid.Min(), solidMax = solid.Max(), largeLandComponents = CountLarge(history.Final.ElevationKm, side),
    landFraction = history.Final.ElevationKm.Count(v => v >= 0) / (double)(side * side),
    mechanics = new {
        algorithm = MembraneCoupling.AlgorithmId, solver = LithosphereMembrane.AlgorithmId,
        materialSide = side, solveSide = mechanical.SolveSide, mechanical.CouplingLength,
        solveStepReferenceUnits = history.ReferenceWidth / mechanical.SolveSide,
        maximumRelativeForceResidual = history.MembraneLedger.Max(s => s.RelativeForceResidual),
        maxIterations = history.MembraneLedger.Max(s => s.Iterations),
        mechanical.Solution.RelativeForceResidual, mechanical.Solution.DrivingWork,
        mechanical.Solution.DragDissipation, mechanical.Solution.ViscousDissipation,
        initialMaterialsIdentical = true, defaultGeneratorUnchanged = true },
    geographicAcceptance = "NOT_ACCEPTED_PENDING_REFERENCE_REVIEW", erosion = "NOT_RUN",
    nativeGame = "NOT_RUN", independentCodeReview = "NOT_RUN",
    limits = new[] { "Viscosity prior is not calibrated to Earth. No mantle temperature columns, slab force, GPE-gradient force or fracture law.",
        "Subduction eligibility still uses imposed plate contrasts and local material normals; actual sinks require solved convergent flux.",
        "Conservative donor transport and old lower-crust redistribution remain unchanged. Numerical diffusion remains.",
        "Mechanical velocities are interpolated from a declared coarse staggered grid. No height is interpolated to manufacture extra detail." }
};
File.WriteAllText(Path.Combine(seedRoot, "manifest.json"), JsonSerializer.Serialize(summary, json));
File.WriteAllText(Path.Combine(seedRoot, "history-ledger.json"), JsonSerializer.Serialize(history.Ledger, json));
File.WriteAllText(Path.Combine(seedRoot, "membrane-ledger.json"), JsonSerializer.Serialize(history.MembraneLedger, json));
File.WriteAllText(Path.Combine(root, $"verification-{seed}.json"), JsonSerializer.Serialize(new {
    status = "PASS_NUMERICAL_ONLY", checksPassed = checks.Length, checks, geographicAcceptance = "NOT_ACCEPTED", reports = new[] { summary }
}, json));
Console.WriteLine($"CHECKS={checks.Length}; seed={seed}; height=[{solid.Min():R},{solid.Max():R}]; maximum force residual={history.MembraneLedger.Max(s => s.RelativeForceResidual):R}; geography=NOT_ACCEPTED");

static double Y(double km) => 168 + 12 * km;
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static void Save(Dictionary<string, object> fields, string directory, string name, IReadOnlyList<double> values, string units)
{
    byte[] bytes = new byte[values.Count * 8];
    for (int i = 0; i < values.Count; i++) {
        if (!double.IsFinite(values[i])) throw new ArithmeticException("Nonfinite exported field.");
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * 8, 8), values[i]); }
    File.WriteAllBytes(Path.Combine(directory, name + ".f64le"), bytes);
    fields.Add(name, new { path = name + ".f64le", sha256 = Hash(bytes), units, minimum = values.Min(), maximum = values.Max(), encoding = "float64 little endian X right Z down" });
}
static int CountLarge(IReadOnlyList<double> values, int n)
{
    bool[] seen = new bool[n*n]; var queue = new Queue<int>(); int large = 0;
    for(int i=0;i<values.Count;i++) {
        if(seen[i] || values[i]<0) continue; seen[i]=true;queue.Enqueue(i);int size=0;
        while(queue.TryDequeue(out int p)) {
            size++; int x=p%n,z=p/n;
            foreach(int j in new[]{z*n+(x+1)%n,z*n+(x+n-1)%n,((z+1)%n)*n+x,((z+n-1)%n)*n+x})
                if(!seen[j] && values[j]>=0){seen[j]=true;queue.Enqueue(j);} }
        if(size>=values.Count/100)large++; }
    return large;
}
