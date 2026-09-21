using System.Globalization;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Tests.L03B;
using ISRWorldGen.Tests.L05A;

namespace ISRWorldGen.Tests.LargeGeography;

/// <summary>
/// Explicit opt-in heavy campaign. This class is not included in the development
/// test project. It calls the same actual Core fixture, over the complete world,
/// never computing an isolated crop with artificial ocean or rainfall boundaries.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LargeExtentDiagnosticTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("LargeSpatialDiagnostics")]
    public void ExportWholeLargeWorldAtActual512SquaredResolution()
    {
        string profileId = Environment.GetEnvironmentVariable("ISR_LARGE_PROFILE") ??
            throw new InvalidOperationException("Select ISR_LARGE_PROFILE: wide-analysis or vast-expeditions.");
        if (!int.TryParse(Environment.GetEnvironmentVariable("ISR_LARGE_SEED"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int seed) || seed is not (-437287116 or 73 or 20260906))
            throw new InvalidOperationException("Select ISR_LARGE_SEED from the fixed review corpus: -437287116, 73, 20260906.");
        string outputRoot = Environment.GetEnvironmentVariable("ISR_SPATIAL_DIAGNOSTICS_ROOT") ??
            throw new InvalidOperationException("Provide an isolated ISR_SPATIAL_DIAGNOSTICS_ROOT for this large campaign.");
        FrozenScaleProfile profile = SelectProfile(profileId);
        new SpatialDiagnosticExportTests { TestContext = TestContext }.Export(seed, true, profile, 512, false);
        string directory = Path.Combine(outputRoot, profile.Id + "-rework-seed-" + seed.ToString(CultureInfo.InvariantCulture));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = metadata.RootElement;
        Assert.AreEqual(512, root.GetProperty("width").GetInt32());
        Assert.AreEqual(512, root.GetProperty("height").GetInt32());
        Assert.AreEqual(profile.WidthBlocks, root.GetProperty("extent").GetProperty("maxXExclusive").GetInt64());
        Assert.AreEqual(profile.LengthBlocks, root.GetProperty("extent").GetProperty("maxZExclusive").GetInt64());
        Assert.AreEqual(profile.RequestedSiteCount, root.GetProperty("atlasSites").GetInt32());
        Assert.IsFalse(root.GetProperty("smallMountainWindowIncluded").GetBoolean());
        Assert.IsFalse(File.Exists(Path.Combine(directory, "mountain-window.json")));
        Assert.AreEqual("NOT_RUN", root.GetProperty("nativeGame").GetString());
        TestContext.WriteLine($"LARGE_WORLD_PROFILE={profile.Id}; seed={seed}; span={profile.WidthBlocks}; samples=262144; step={profile.WidthBlocks / 512}");
    }

    [TestMethod]
    public void ProfilesKeepExplicitDimensionsBudgetsAndRealSampleSpacing()
    {
        var wide = SelectProfile("wide-analysis"); var vast = SelectProfile("vast-expeditions");
        Assert.AreEqual(262_144L, wide.WidthBlocks); Assert.AreEqual(512L, wide.WidthBlocks / 512);
        Assert.AreEqual(1_024_000L, vast.WidthBlocks); Assert.AreEqual(2_000L, vast.WidthBlocks / 512);
        Assert.AreEqual(128, wide.RequestedSiteCount); Assert.AreEqual(128, vast.RequestedSiteCount);
        Assert.ThrowsExactly<ArgumentException>(() => SelectProfile("unknown"));
    }

    private static FrozenScaleProfile SelectProfile(string id)
    {
        if (id == "vast-expeditions") return L03BTestSupport.FrozenProfile(id);
        if (id != "wide-analysis") throw new ArgumentException("Unknown explicit diagnostic profile.", nameof(id));
        ScaleProfileDefinition basis = ScaleProfileCatalog.Proposals.Single(profile => profile.Id == "balanced");
        // Diagnostic proposal only, never a replacement of a player's frozen profile.
        // A different world profile is not a same-world zoom or a matched-density test.
        ScaleProfileDefinition proposal = basis with
        {
            Id = "wide-analysis", WidthBlocks = 262_144, LengthBlocks = 262_144,
            AtlasResolutionBlocks = 23_170, RequestedSiteCount = 128, SiteQuota = 256,
            AtlasMemoryBudgetBytes = 2L * 1024 * 1024 * 1024,
        };
        var assumedLimits = new NativeWorldConstraints("large-diagnostic-limits-not-live-audit", 1,
            [256, 384, 512], 4_096, 1_024_000, 512);
        return L03BTestSupport.Success(ScaleProfileValidator.ValidateAndFreeze(proposal, assumedLimits));
    }
}
