using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

if (args.Length is < 3 or > 4 || !int.TryParse(args[1], out int side) || side is < 64 or > 512 || (side & (side - 1)) != 0 || !int.TryParse(args[2], out int selectedSeed))
    throw new ArgumentException("Usage: WorldGen.Rheology <new-output-directory> <side64..512> <seed> [homogeneous|heterogeneous|powerlaw]");
string mode = args.Length == 4 ? args[3] : "heterogeneous";
if (mode is not ("homogeneous" or "heterogeneous" or "powerlaw")) throw new ArgumentException("Unknown comparison mode.");
string root = Path.GetFullPath(args[0]);
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Evidence directory exists; never overwrite an earlier campaign.");
string[] checks = TectonicChecks.Run().Concat(PolarityRegressionChecks.Run()).Concat(MaterialCohortChecks.Run()).Concat(OceanCarrierChecks.Run()).Concat(AssemblageChecks.Run()).Concat(DeformationChecks.Run()).Concat(PowerLawChecks.Run()).ToArray();
Directory.CreateDirectory(root);
var json = new JsonSerializerOptions { WriteIndented = true };
var reports = new List<object>();
foreach (int seed in new[] { selectedSeed })
{
    var reference = new TectonicScalePlan(1_000_000, 1_000_000);
    var settings = new TectonicEvolutionSettings(side: side);
    // Match the old CONTINENTAL VOLUME for this seed; do not obtain diversity by adding mass or moving sea level.
    var oldInitial = MaterialBoundHistory.Generate(seed, reference, new TectonicEvolutionSettings(side: side, duration: 0));
    double initialBudget = CrustTransport.Sum(oldInitial.Initial.ContinentalKm) / (side * side);
    var assemblage = ContinentalAssemblage.Generate(seed, reference, side, initialBudget);
    var rheology = new SheetRheologyOptions(homogeneousControl: mode == "homogeneous",
        powerLaw: mode == "powerlaw" ? new PowerLawSheetOptions() : null);
    MaterialBoundHistory history = MaterialBoundHistory.GenerateWithRheology(seed, reference, settings, assemblage, rheology);
    MaterialDeformationFrame finalMechanics = history.FinalDeformation!;
    int components = CountLargeLandComponents(history.Final.ElevationKm, side);
    string seedRoot = Path.Combine(root, "seed-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    Directory.CreateDirectory(seedRoot);
    var fields = new Dictionary<string, object>();
    WriteField("legacy-initial-height", oldInitial.Initial.ElevationKm.Select(v => 168 + 12 * v).ToArray(), "old equal-quota initial solid Y; SAME continental volume");
    WriteField("initial-continental-thickness", assemblage.ContinentalKm, "new equivalent continental thickness km BEFORE transport");
    WriteField("initial-provinces", assemblage.ProvinceIds.Select(v => (double)v).ToArray(), "initial material provinces -1=ocean; NOT mechanical plates; boundaries before margin mixing");
    // Auxiliary mechanical fields carry their REAL coarse resolution in the manifest.
    int mechSide = finalMechanics.MechanicalSide;
    double[] viscosityDisplay = Enumerable.Range(0, side * side).Select(i => finalMechanics.RelativeViscosity[(i / side / (side / mechSide)) * mechSide + (i % side / (side / mechSide))]).ToArray();
    WriteField("relative-viscosity", viscosityDisplay, "dimensionless constitutive prior; computed at mechanicalSide; repeated only for display, not fine rheology");
    WriteField("velocity-east", finalMechanics.East, "reference units/model time; MAC east faces reconstructed from mechanical grid");
    WriteField("velocity-south", finalMechanics.South, "reference units/model time; MAC south faces reconstructed from mechanical grid");
    WriteField("instant-divergence", finalMechanics.Divergence, "1/model time from actual transported face velocities");
    WriteField("initial-height", history.Initial.ElevationKm.Select(v => 168 + 12 * v).ToArray(), "solid Y blocks, fixed 12 blocks/model-km; no water");
    WriteField("height", history.Final.ElevationKm.Select(v => 168 + 12 * v).ToArray(), "solid Y blocks, fixed 12 blocks/model-km; no water");
    WriteField("continental-thickness", history.Final.ContinentalKm, "km equivalent continental crust");
    WriteField("oceanic-thickness", history.Final.OceanicKm, "km equivalent oceanic crust");
    WriteField("ocean-age", history.Final.OceanAge, "mean model-time age of advected oceanic volume; 0 where absent");
    WriteField("new-ocean-fraction", history.Final.OceanicKm.Select((v, i) => v > 0 ? 1 - history.Final.InheritedOceanicKm[i] / v : 0).ToArray(), "fraction of current oceanic volume formed during this history");
    WriteField("compression", history.Final.AccumulatedCompression, "accumulated negative divergence; Eulerian diagnostic");
    WriteField("extension", history.Final.AccumulatedExtension, "accumulated positive divergence; Eulerian diagnostic");
    WriteField("shear", history.Final.AccumulatedShear, "accumulated shear strain; Eulerian diagnostic");
    WriteField("plates", history.Final.PlateIds.Select(v => (double)v).ToArray(), "dominant transported material origin; not a retessellated seed label");
    WriteField("owner-fraction", history.FinalOwnerFraction, "fraction of local crust from the dominant origin; mixing is not hidden");
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
        seed, mode, algorithm = "rheology-comparison-v2/" + MaterialBoundHistory.AlgorithmId, commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "UNVERIFIED_WORKTREE",
        initialAssemblage = new { algorithm = ContinentalAssemblage.AlgorithmId, assemblage.Checksum, assemblage.Provinces,
            assemblage.MeanContinentalKm, assemblage.MarginWidthReferenceUnits, assemblage.ThicknessBudgetMultiplier,
            basis = "explicit heterogeneous initial-state prior, NOT a simulated Earth assembly history", matchedContinentalVolume = true },
        historyChecksum = history.Checksum, initialChecksum = history.Initial.Checksum, finalChecksum = history.Final.Checksum,
        coreAssemblySha256 = Hash(File.ReadAllBytes(typeof(MaterialBoundHistory).Assembly.Location)),
        scope = "REDUCED_VISCOUS_SHEET_BASAL_DRAG_NOT_COMPLETE_PLATE_DYNAMICS", geographicAcceptance = "NOT_ACCEPTED_PENDING_REFERENCE_REVIEW",
        nativeGame = "NOT_RUN", erosion = "NOT_RUN_GATED_ON_RELIEF_REVIEW", independentCodeReview = "NOT_RUN",
        boundary = "PERIODIC_PLANAR_PREPARATORY_ATLAS; does not imply native player wrapping",
        worldWidthBlocks = 1_000_000, worldLengthBlocks = 1_000_000, history.ReferenceWidth, history.ReferenceLength,
        referenceKmPerUnit = MaterialBoundHistory.ReferenceKmPerUnit, worldHeightBlocks = 384, seaLevelReferenceBlocks = 168,
        blocksPerModelKm = 12, width = side, height = side, settings, scaleChecks, fields,
        rheology, plates = history.Plates, mechanicalSide = mechSide, mechanicalStepReferenceUnits = history.ReferenceWidth / mechSide,
        mechanicalPolicy = SheetRheologyOptions.AlgorithmId, equilibriumOperator = rheology.PowerLaw is null ? ThinSheetDeformation.AlgorithmId : PowerLawSheetDeformation.AlgorithmId,
        mechanicalSolves = history.MechanicalSolves, maxForceResidual = history.MechanicalSolves.Max(s => s.RelativeResidual),
        forceInterpretation = "v-div(2mu(eps+trace(eps)I))=preferred material velocity; constant normalized basal drag; length=DeformationWidth",
        viscosityPrior = "mu0: homogeneous=1 or harmonic material/age mixture; powerlaw=mu0*(1+Q/(2*(L*rate0)^2))^(-(1-1/n)/2), rate0 per MODEL time; NOT calibrated Earth rheology",
        causalComparison = "same initial materials, prescribed plate velocities, L=DeformationWidth, grids and mechanical schedule; subsequent forcing follows each evolving material state",
        maximumNewtonIterations = history.MechanicalSolves.Max(s => s.NonlinearIterations),
        physicalLimitations = "No GPE forcing, slab pull, mantle heat equation, damage, rigid-plate torques or new fragmentation law; existing subduction polarity unchanged",
        history.InitialMaterialChecksum, history.FinalMaterialChecksum, history.UnresolvedInterfaceFaces, history.InitialContinentalByOrigin, history.FinalContinentalByOrigin,
        waterSurfacePresent = false, seabedMasked = false, perImageAutoContrast = false,
        steps = history.Ledger.Count - 1, initial = history.Ledger[0], final = history.Ledger[^1],
        solidMin = solid.Min(), solidMax = solid.Max(), largeLandComponents = components,
        landFraction = history.Final.ElevationKm.Count(v => v >= 0) / (double)(side * side),
        solverNotes = new[] { "First-order donor-cell numerical diffusion remains measurable.",
            "Inherited domain-switch settings are not used: this separate algorithm always follows material origins with metric velocity support.",
            "Velocity now balances basal drag and viscous membrane stresses with material-dependent coefficients. No random total plate weight is used.",
            "Voronoi is used only at time zero; all material origins are subsequently transported by the donor fluxes. This remains a mixed thin-sheet approximation, not rigid Lagrangian plates.",
            "Subduction recycles volume on selected lower sides, but flexural trench/arc mechanics are not implemented.",
            "No tectonic phase is hidden in a coloured rendering. No Earth data is copied into the generated height field." }
    };
    File.WriteAllText(Path.Combine(seedRoot, "manifest.json"), JsonSerializer.Serialize(summary, json));
    File.WriteAllText(Path.Combine(seedRoot, "history-ledger.json"), JsonSerializer.Serialize(history.Ledger, json));
    File.WriteAllText(Path.Combine(seedRoot, "mechanical-ledger.json"), JsonSerializer.Serialize(history.MechanicalSolves, json));
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
File.WriteAllText(Path.Combine(root, "verification-" + selectedSeed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json"), JsonSerializer.Serialize(new
{
    mode, status = "PASS_NUMERICAL_ONLY", algorithm = SheetRheologyOptions.AlgorithmId, checksPassed = checks.Length, checks,
    context = "complete reference atlas at 1000000 units mapped without cropping to 131072, 262144 and 1000000 blocks",
    geographicAcceptance = "NOT_ACCEPTED", erosion = "NOT_RUN", nativeGame = "NOT_RUN", reports
}, json));
Console.WriteLine($"TECTONIC_CHECKS={checks.Length}; one generated full history; no geographic acceptance implied.");

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
