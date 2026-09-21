using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

// Explicit opt-in campaign: no climate, water, incision, erosion, game or saves.
if (args.Length != 2 || !int.TryParse(args[1], out int side) || side is < 64 or > 2048 || (side & (side - 1)) != 0)
    throw new ArgumentException("Usage: WorldGen.RawRelief <new-output-directory> <power-of-two-side 64..2048>");
string root = Path.GetFullPath(args[0]);
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Output exists; evidence is never overwritten.");
Directory.CreateDirectory(root);
var reports = new List<object>();
foreach (int seed in new[] { -437287116, 73, 20260906 })
{
    var f = Build(seed); var model = f.Model;
    int checks = 0;
    var points = Enumerable.Range(0, 256).Select(i => new WorldBlockPosition((i * 65537L + 63) % f.Width, (i * 98317L + 91) % f.Length)).ToArray();
    var expected = points.Select(p => model.Sample(p.X, p.Z)).ToArray();
    for (int i = points.Length - 1; i >= 0; i--) Check(expected[i] == model.Sample(points[i].X, points[i].Z), "Sample-order difference");
    Parallel.For(0, points.Length, i =>
    { if (expected[i] != model.Sample(points[i].X, points[i].Z)) throw new InvalidOperationException("Concurrent sampling differs"); });
    Refuse(() => model.Sample(-1, 0)); Refuse(() => model.Sample(f.Width, 0));
    var other = Build(seed == 73 ? 74 : 73);
    Refuse(() => RawReliefModel.Build(f.Basis, other.Atlas, f.Plates, f.Continents));
    Check(model.ContentChecksum != other.Model.ContentChecksum, "Seed-blind model identity");
    var repeated = RawReliefModel.Build(f.Basis, f.Atlas, f.Plates, f.Continents);
    Check(model.ContentChecksum == repeated.ContentChecksum, "Rebuilt checksum differs");
    foreach (var p in points) Check(model.Sample(p.X, p.Z) == repeated.Sample(p.X, p.Z), "Rebuilt field differs");
    int count = checked(side * side); var values = new double[count];
    Parallel.For(0, side, row =>
    {
        long z = (2L * row + 1) * f.Length / (2L * side);
        for (int col = 0; col < side; col++)
        {
            long x = (2L * col + 1) * f.Width / (2L * side);
            var sample = model.Sample(x, z);
            if (!double.IsFinite(sample.HeightBlocks) || sample.HeightBlocks < 0 || sample.HeightBlocks >= f.Height || sample.SignedHeightAboveSea != sample.HeightBlocks - f.Sea)
                throw new InvalidOperationException("Invalid unmasked bedrock height");
            values[row * side + col] = sample.HeightBlocks;
        }
    });
    var below = values.Where(v => v < f.Sea).ToArray(); var above = values.Where(v => v >= f.Sea).ToArray();
    Check(below.Length > 0 && above.Length > 0, "Reference corpus lost land or seabed");
    Check(below.Distinct().Take(1024).Count() == 1024, "Seabed was collapsed or prematurely quantized");
    Check(values.All(v => v != f.Sea), "Water-surface substitution detected");
    int corner = side / 4;
    for (int r = corner; r < 3 * corner; r += Math.Max(1, side / 32))
    for (int c = corner; c < 3 * corner; c += Math.Max(1, side / 32))
        Check(values[r * side + c] == model.Sample((2L * c + 1) * f.Width / (2L * side), (2L * r + 1) * f.Length / (2L * side)).HeightBlocks, "Context-window discrepancy");
    byte[] raw = new byte[checked(count * 8)];
    for (int i = 0; i < count; i++) BinaryPrimitives.WriteDoubleLittleEndian(raw.AsSpan(i * 8, 8), values[i]);
    string directory = Path.Combine(root, "seed-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "height.f64le"), raw);
    var sorted = values.Order().ToArray();
    var meta = new
    {
        schemaVersion = 1, scope = "UNERODED_RAW_BEDROCK_HEIGHT", numericStatus = "PASS",
        geographicAcceptance = "PENDING_EARTH_REFERENCE_REVIEW", erosion = "NOT_RUN_GATED_ON_RELIEF_REVIEW",
        nativeGame = "NOT_RUN", independentReview = "NOT_RUN", seed,
        commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "WORKING_TREE_UNVERIFIED",
        algorithm = RawReliefModel.AlgorithmId, modelChecksum = model.ContentChecksum.ToString(),
        coreAssemblySha256 = Hash(File.ReadAllBytes(typeof(RawReliefModel).Assembly.Location)),
        width = side, height = side, worldWidthBlocks = f.Width, worldLengthBlocks = f.Length,
        worldHeightBlocks = f.Height, seaLevelReferenceBlocks = f.Sea,
        minX = 0, minZ = 0, maxXExclusive = f.Width, maxZExclusive = f.Length,
        stepX = f.Width / side, stepZ = f.Length / side,
        sampleOriginX = f.Width / side / 2, sampleOriginZ = f.Length / side / 2,
        encoding = "float64 little-endian; row-major; actual solid Y; X right, positive Z down",
        fieldsSha256 = Hash(raw), byteLength = raw.Length, seabedMasked = false, waterSurfacePresent = false,
        verticalBudget = new { maximumOceanDepth = 80, minimumCavernInteriorHeight = 48, maximumReliefAboveSea = 184 },
        fixture = "balanced; L03B analytical constraints, not a new native audit; 64 sites, 7 plates; 5 macro + 18 regional crust envelopes",
        checks, model.CollisionBelts, model.DivergentBelts,
        statistics = new { minimum = sorted[0], maximum = sorted[^1], p01 = sorted[count / 100], p50 = sorted[count / 2], p99 = sorted[99 * count / 100],
            belowReferenceSea = below.Length, aboveReferenceSea = above.Length, seafloorMinimum = below.Min(), seafloorMaximum = below.Max(),
            landMaximumAboveSea = above.Max() - f.Sea },
        palettes = new { minHeight = 0, maxHeight = f.Height - 1, grey16Decode = "height = code * 383 / 65535", maximumError = 383d / 131070,
            perImageAutoContrast = false, colouredPreviewIsNotHeightData = true }
    };
    File.WriteAllBytes(Path.Combine(directory, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(meta, new JsonSerializerOptions { WriteIndented = true }));
    reports.Add(meta);
    Console.WriteLine($"seed={seed} samples={count} min={sorted[0]:R} max={sorted[^1]:R} seabed={below.Length} checks={checks}; geographicReview=PENDING");
    void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks++; }
    void Refuse(Action action)
    {
        try { action(); } catch (ArgumentException) { checks++; return; }
        throw new InvalidOperationException("An invalid input was accepted");
    }
}
File.WriteAllBytes(Path.Combine(root, "campaign.json"), JsonSerializer.SerializeToUtf8Bytes(reports, new JsonSerializerOptions { WriteIndented = true }));

static Fixture Build(int seed)
{
    var definition = ScaleProfileCatalog.Proposals.Single(p => p.Id == "balanced");
    var profile = Success(ScaleProfileValidator.ValidateAndFreeze(definition,
        new NativeWorldConstraints("l03b-qualified-native-v1", 1, [256, 384, 512], 4096, 1_024_000, 512)));
    var identity = new GenerationIdentity(seed, GenerationIdentity.SupportedAlgorithmVersion, GenerationIdentity.SupportedSchemaVersion,
        profile.GeographyConfigHash, Hash256.Parse("cfd370ef8c6540792e17507897d0db5a9d96b621a805d1ccc605abcf89460ddf"), $"l03b-{profile.Id}-v{profile.ProfileVersion}");
    var bounds = new WorldBounds(0, 0, profile.WidthBlocks, profile.LengthBlocks);
    var sites = Success(AtlasSiteGenerator.Generate(identity, bounds, new AtlasSiteGenerationSettings(profile.RequestedSiteCount)));
    var atlas = Success(AtlasGeometryBuilder.Build(identity, bounds, sites.Sites, new AtlasGeometryBuildOptions(1, GeometryCacheMode.Cold)));
    var settings = new ContinentalFieldSettings(5, 18, 64, 1_000_000);
    var continents = Success(ContinentalFieldModel.Create(identity, bounds, settings));
    var plates = Success(PlateAtlasBuilder.Build(identity, atlas, profile, new PlateGenerationSettings(7, settings, profile.SiteQuota, 4000, 4_000_000)));
    var basis = Success(LandscapeModelBuilder.Build(identity, atlas, plates, profile,
        new LandscapeGenerationSettings(new ReliefBudgetRequest(80, 48, 184), profile.SiteQuota, 1.25)));
    return new Fixture(RawReliefModel.Build(basis, atlas, plates, continents), basis, atlas, plates, continents,
        profile.WidthBlocks, profile.LengthBlocks, profile.HeightBlocks, basis.VerticalPlan.SeaLevelBlocks);
}
static T Success<T>(GenerationResult<T> result) where T : class => result is GenerationSuccess<T> success
    ? success.Snapshot : throw new InvalidOperationException("Builder refused fixture: " + result);
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
internal sealed record Fixture(RawReliefModel Model, LandscapeModel Basis, AtlasMesh Atlas, PlateAtlasSnapshot Plates,
    ContinentalFieldModel Continents, long Width, long Length, int Height, long Sea);
