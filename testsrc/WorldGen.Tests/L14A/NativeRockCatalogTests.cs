using ISRWorldGen.Compatibility.RockCatalog;

namespace ISRWorldGen.Tests.L14A;

[TestClass]
public sealed class NativeRockCatalogTests
{
    [TestMethod]
    public void T14_01_EffectiveInventoryIsSortedImmutableAndFingerprintIsOrderIndependent()
    {
        NativeRockCatalogSnapshot forward = CreateCatalog();
        NativeRockCatalogSnapshot reversed = CreateCatalog(reverse: true);

        CollectionAssert.AreEqual(new[] { "game:rock-granite", "game:rock-limestone" }, forward.Rocks.Select(rock => rock.NativeCode).ToArray());
        Assert.AreEqual(forward.Fingerprint, reversed.Fingerprint);
        CollectionAssert.AreEqual(new[] { "game:blocktypes/stone/granite.json", "game:blocktypes/stone/limestone.json" }, forward.EffectiveAssets.Select(asset => asset.AssetLocation).ToArray());
        StringAssert.Contains(forward.CreateCanonicalExport(), "admitted-provenance=game");
        StringAssert.Contains(forward.CreateCanonicalExport(), "asset=game:blocktypes/stone/granite.json;provenance=game;revision=r1");
        StringAssert.Contains(forward.CreateCanonicalExport(), "host=game:cassiterite;rock=granite;requirement=Required");
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<NativeRockDefinition>)forward.Rocks).Add(Rock("basalt", "game:rock-basalt", NativeRockFamily.Igneous)));
    }

    [TestMethod]
    public void T14_01_RejectsDuplicateIdsMissingAssetsAndMixedProvenanceOrRevision()
    {
        NativeAssetRecord[] assets = Assets();
        NativeRockDefinition granite = Rock("granite", "game:rock-granite", NativeRockFamily.Igneous);
        NativeRockDefinition limestone = Rock("limestone", "game:rock-limestone", NativeRockFamily.Sedimentary);

        Assert.ThrowsExactly<ArgumentException>(() => Build(assets, [granite, granite], Hosts(), []));
        Assert.ThrowsExactly<ArgumentException>(() => Build(assets, [new NativeRockDefinition("granite", "game:rock-granite", NativeRockFamily.Igneous, "game:blocktypes/stone/missing.json", "game", 1d), limestone], Hosts(), []));
        Assert.ThrowsExactly<ArgumentException>(() => Build([.. assets, new NativeAssetRecord("mod:blocktypes/stone/other.json", "mod", "r2")], [granite, limestone], Hosts(), []));
        Assert.ThrowsExactly<ArgumentException>(() => Build(assets, [new NativeRockDefinition("granite", "game:rock-granite", NativeRockFamily.Igneous, "game:blocktypes/stone/granite.json", "mod", 1d), limestone], Hosts(), []));
    }

    [TestMethod]
    public void T14_01_RequiredCodesAndEffectiveAssetChangesAreBlockingAndFingerprinted()
    {
        NativeRockCatalogSnapshot baseline = CreateCatalog();
        NativeAssetRecord[] modifiedAssets = [
            new("game:blocktypes/stone/granite.json", "game", "r1"),
            new("game:blocktypes/stone/limestone.json", "game", "r1"),
            new("game:blocktypes/stone/basalt.json", "game", "r1"),
        ];
        NativeRockCatalogSnapshot modified = Build(modifiedAssets,
            [Rock("limestone", "game:rock-limestone", NativeRockFamily.Sedimentary), Rock("granite", "game:rock-granite", NativeRockFamily.Igneous)], Hosts(), []);

        Assert.AreNotEqual(baseline.Fingerprint, modified.Fingerprint);
        Assert.ThrowsExactly<ArgumentException>(() => Build(Assets(), [Rock("limestone", "game:rock-limestone", NativeRockFamily.Sedimentary)], Hosts(), []));
    }

    [TestMethod]
    public void T14_01_ExternalHostFamilyRequirementRejectsAbsentCompatibleHost()
    {
        NativeRockDefinition[] rocks = [Rock("limestone", "game:rock-limestone", NativeRockFamily.Sedimentary), Rock("granite", "game:rock-granite", NativeRockFamily.Igneous)];
        NativeRockCatalogManifest missingMetamorphicHost = new(
            "isr-rocks-v1", "vintagestory-1.22.7", "r1", ["game"], ["game:rock-granite"],
            [new NativeHostFamilyRequirement("game:cassiterite", NativeRockFamily.Metamorphic)]);

        Assert.ThrowsExactly<ArgumentException>(() => new NativeRockCatalogSnapshot(missingMetamorphicHost, Assets(), rocks, Hosts(), []));
    }

    [TestMethod]
    public void T14_01_RejectsForbiddenMineralHostAndNonFiniteWeights()
    {
        NativeRockDefinition granite = Rock("granite", "game:rock-granite", NativeRockFamily.Igneous);
        NativeRockDefinition limestone = Rock("limestone", "game:rock-limestone", NativeRockFamily.Sedimentary);

        Assert.ThrowsExactly<ArgumentException>(() => Build(Assets(), [granite, limestone], Hosts(), [new NativeResourceHost("game:cassiterite", "granite", NativeHostRequirement.Optional)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new NativeRockDefinition("granite", "game:rock-granite", NativeRockFamily.Igneous, "game:blocktypes/stone/granite.json", "game", double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new NativeRockDefinition("granite", "game:rock-granite", NativeRockFamily.Igneous, "game:blocktypes/stone/granite.json", "game", double.PositiveInfinity));
    }

    private static NativeRockCatalogSnapshot CreateCatalog(bool reverse = false)
    {
        NativeAssetRecord[] assets = Assets();
        NativeRockDefinition[] rocks = [Rock("limestone", "game:rock-limestone", NativeRockFamily.Sedimentary), Rock("granite", "game:rock-granite", NativeRockFamily.Igneous)];
        NativeResourceHost[] hosts = Hosts();
        return Build(reverse ? assets.Reverse() : assets, reverse ? rocks.Reverse() : rocks, reverse ? hosts.Reverse() : hosts, []);
    }

    private static NativeRockCatalogSnapshot Build(IEnumerable<NativeAssetRecord> assets, IEnumerable<NativeRockDefinition> rocks, IEnumerable<NativeResourceHost> hosts, IEnumerable<NativeResourceHost> forbidden) =>
        new(Manifest(), assets, rocks, hosts, forbidden);

    private static NativeRockCatalogManifest Manifest() => new(
        "isr-rocks-v1", "vintagestory-1.22.7", "r1", ["game"],
        ["game:rock-granite", "game:rock-limestone"],
        [new NativeHostFamilyRequirement("game:cassiterite", NativeRockFamily.Igneous)]);

    private static NativeAssetRecord[] Assets() =>
    [
        new("game:blocktypes/stone/granite.json", "game", "r1"),
        new("game:blocktypes/stone/limestone.json", "game", "r1"),
    ];

    private static NativeRockDefinition Rock(string key, string code, NativeRockFamily family) =>
        new(key, code, family, $"game:blocktypes/stone/{key}.json", "game", 1d);

    private static NativeResourceHost[] Hosts() =>
    [
        new("game:cassiterite", "granite", NativeHostRequirement.Required),
        new("game:coal", "limestone", NativeHostRequirement.Optional),
    ];
}
