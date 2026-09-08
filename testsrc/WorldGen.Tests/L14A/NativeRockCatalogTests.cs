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
        new("isr-rocks-v1", "vintagestory-1.22.7", "r1", assets, rocks, hosts, forbidden);

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
