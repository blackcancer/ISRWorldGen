using System.Text;
using ISRWorldGen.Compatibility.DepositCatalog;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Stratigraphy;
using ISRWorldGen.Core.Minerals.CatalogAndPotential;

namespace ISRWorldGen.Tests.L16A;

[TestClass]
public sealed class DepositCatalogAndPotentialTests
{
    [TestMethod]
    public void T16_01_ExtractsVersionedSemanticRulesAndRefusesMissingRequiredHosts()
    {
        ContentCatalogSnapshot content = Content();
        NativeDepositCatalogManifest manifest = Manifest(content);
        DepositCatalogSnapshot forward = NativeDepositCatalogExtractor.Extract(manifest, content, Rules());
        DepositCatalogSnapshot reversed = NativeDepositCatalogExtractor.Extract(manifest, content, Rules().Reverse());

        DepositDefinition cassiterite = forward.Get("game:cassiterite");
        Assert.AreEqual("deposit-r1", forward.CatalogRevision);
        Assert.AreEqual(content.CatalogId, forward.CatalogId);
        CollectionAssert.AreEqual(new[] { "granite" }, cassiterite.AllowedHostRockKeys.ToArray());
        Assert.AreEqual(DepositAltitudeReference.DepthBelowSurface, cassiterite.AltitudeRange.Reference);
        Assert.AreEqual(48d, cassiterite.NativeParameters.MaximumSize);
        Assert.AreEqual(forward.Fingerprint, reversed.Fingerprint, "Input order must not affect the versioned export.");
        DepositAltitudeContext altitudeContext = new(-64, 320, 110, 180);
        DepositAbsoluteAltitudeRange depthRange = DepositAltitudeMapper.Resolve(cassiterite.AltitudeRange, altitudeContext);
        Assert.AreEqual(116d, depthRange.MinimumInclusiveY);
        Assert.AreEqual(172d, depthRange.MaximumInclusiveY);
        Assert.AreEqual(110d, DepositAltitudeMapper.Resolve(new(DepositAltitudeReference.SeaLevelRelativeY, 0d, 0d), altitudeContext).MinimumInclusiveY);
        Assert.AreEqual(-64d, DepositAltitudeMapper.Resolve(new(DepositAltitudeReference.WorldHeightFraction, 0d, 0d), altitudeContext).MinimumInclusiveY);

        Assert.ThrowsExactly<ArgumentException>(() => NativeDepositCatalogExtractor.Extract(manifest, content, Rules().Where(rule => rule.ResourceKey != "game:coal")));
        Assert.ThrowsExactly<ArgumentException>(() => NativeDepositCatalogExtractor.Extract(manifest, content, [Rule("game:cassiterite", "limestone", "game:deposit/cassiterite.json"), Rule("game:coal", "limestone", "game:deposit/coal.json")]));
        Assert.ThrowsExactly<ArgumentException>(() => NativeDepositCatalogExtractor.Extract(manifest, content, [Rule("game:cassiterite", "granite", "game:deposit/cassiterite.json", "game:coal"), Rule("game:coal", "limestone", "game:deposit/coal.json", "game:cassiterite")]));
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeDepositCatalogExtractor.Extract(Manifest(Content("content-r2")), content, Rules()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new NativeDepositParameters(double.PositiveInfinity, 1d, 2d, 1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DepositAltitudeRange(DepositAltitudeReference.WorldHeightFraction, 0d, double.NaN));
    }

    [TestMethod]
    public void T16_02_PotentialUsesPublishedGeologyAndIsStableAcrossOrderChunksAndWorkers()
    {
        (GeologyVolumeSnapshot granite, DepositCatalogSnapshot catalog) = Fixture();
        var model = new MineralPotentialModel(catalog, Profile(), granite.Identity);
        MineralPotentialSnapshot baseline = model.Evaluate(granite, 18, 64, -9);
        MineralPotentialSnapshot[] reordered = new[] { (18L, 64, -9L), (3L, 64, 2L), (-7L, 64, 4L) }
            .Reverse().AsParallel().WithDegreeOfParallelism(2).Select(point => model.Evaluate(granite, point.Item1, point.Item2, point.Item3)).OrderBy(snapshot => snapshot.Readings[0].ResourceKey, StringComparer.Ordinal).ToArray();
        MineralPotentialSnapshot repeated = model.Evaluate(granite, 18, 64, -9);

        MineralPotentialReading cassiterite = baseline.Readings.Single(reading => reading.ResourceKey == "game:cassiterite");
        MineralPotentialReading coal = baseline.Readings.Single(reading => reading.ResourceKey == "game:coal");
        Assert.IsTrue(cassiterite.HostIsAdmissible);
        Assert.IsTrue(cassiterite.Intensity > 0d && cassiterite.Intensity < 1d);
        Assert.IsFalse(coal.HostIsAdmissible, "A forbidden host produces zero potential, not a fallback host.");
        Assert.AreEqual(0d, coal.Intensity);
        CollectionAssert.AreEqual(baseline.Readings.ToArray(), repeated.Readings.ToArray());
        Assert.IsTrue(reordered.All(snapshot => snapshot.GeologyFingerprint == granite.Fingerprint && snapshot.DepositCatalogFingerprint == catalog.Fingerprint));
        Assert.AreEqual(granite.Identity.GeologyRevision, baseline.GeologyRevision);
        Assert.AreEqual(granite.Identity.Fingerprint, baseline.GeologyFingerprint);
    }

    [TestMethod]
    public void T16_02_GeologicalPropertyChangeChangesFixtureAndRevisionGateRejectsMixedInputs()
    {
        (GeologyVolumeSnapshot granite, DepositCatalogSnapshot catalog) = Fixture();
        GeologyVolumeSnapshot alteredGranite = Geology(Content("content-r2", erosion: 0.2d, solubility: 0.8d, permeability: 0.6d));
        var model = new MineralPotentialModel(catalog, Profile(), granite.Identity);
        double original = model.Evaluate(granite, 0, 64, 0).Readings.Single(value => value.ResourceKey == "game:cassiterite").Intensity;

        Assert.AreNotEqual(granite.Fingerprint, alteredGranite.Fingerprint);
        Assert.ThrowsExactly<InvalidOperationException>(() => model.Evaluate(alteredGranite, 0, 64, 0));
        DepositCatalogSnapshot alteredCatalog = Deposit(NativeDepositCatalogExtractor.Extract(Manifest(alteredGranite.Catalog), alteredGranite.Catalog, Rules()));
        Assert.AreNotEqual(original, new MineralPotentialModel(alteredCatalog, Profile(), alteredGranite.Identity)
            .Evaluate(alteredGranite, 0, 64, 0).Readings.Single(value => value.ResourceKey == "game:cassiterite").Intensity);
        Assert.ThrowsExactly<InvalidOperationException>(() => model.Evaluate(new ForgedGeology(granite), 0, 64, 0));
    }

    private static (GeologyVolumeSnapshot Geology, DepositCatalogSnapshot Catalog) Fixture()
    {
        ContentCatalogSnapshot content = Content();
        return (Geology(content), NativeDepositCatalogExtractor.Extract(Manifest(content), content, Rules()));
    }

    private static DepositCatalogSnapshot Deposit(DepositCatalogSnapshot catalog) => catalog;
    private static ContentCatalogSnapshot Content(string revision = "content-r1", double erosion = 0.9d, double solubility = 0.05d, double permeability = 0.1d) => new(
        "catalog-r1", "vintagestory-1.22.7", revision, Hash256.Compute(Encoding.UTF8.GetBytes($"{revision}|{erosion:R}|{solubility:R}|{permeability:R}")),
        [new("granite", "game:rock-granite", RockFamily.Igneous, new(erosion, solubility, permeability)), new("limestone", "game:rock-limestone", RockFamily.Sedimentary, new(0.4d, 0.7d, 0.3d))]);
    private static GeologyVolumeSnapshot Geology(ContentCatalogSnapshot content) => new("geo-r1", content, "blocks-r1", 0, 128,
        [new(Id(1), "granite", 1, new(128d, 0d, 0d), 128d)], [], []);
    private static NativeDepositCatalogManifest Manifest(ContentCatalogSnapshot content) => new(content.CatalogId, content.TargetVersion, "deposit-r1", content.Fingerprint, ["game"], ["game:cassiterite", "game:coal"], [new("game:cassiterite", RockFamily.Igneous)]);
    private static NativeDepositRule[] Rules() => [Rule("game:cassiterite", "granite", "game:deposit/cassiterite.json"), Rule("game:coal", "limestone", "game:deposit/coal.json")];
    private static NativeDepositRule Rule(string resource, string host, string asset, string? parent = null) => new(resource, "game", asset, [host], new(DepositAltitudeReference.DepthBelowSurface, 8d, 64d), new(0.2d, 4d, 48d, 0.6d), "native-vein", parent, DepositSurfaceIndicator.NativeProbabilistic, "regional", "game:guide-minerals");
    private static MineralPotentialProfile Profile() => new("potential-r1", [new KeyValuePair<string, GeologicalPotentialCoefficients>("game:cassiterite", new(1d, 1d, 2d, 0.8d)), new KeyValuePair<string, GeologicalPotentialCoefficients>("game:coal", new(1d, 0d, 1d, 0.8d))]);
    private static StableId Id(ulong ordinal) => StableId.Derive(RandomDomain.Geology, StableId.Zero, 16_000 + ordinal);

    private sealed class ForgedGeology(GeologyVolumeSnapshot inner) : IGeologyVolumeQuery
    {
        public GeologyRevisionIdentity Identity => inner.Identity;
        public GeologySample SampleRock(long x, int y, long z)
        {
            GeologySample sample = inner.SampleRock(x, y, z);
            return sample with { Fingerprint = Hash256.Zero };
        }
    }
}
