using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L01A;

[TestClass]
public sealed class DeterministicIdentityTests
{
    private static readonly StableId ReferenceStream = StableId.Parse("00112233445566778899aabbccddeeff");

    [TestMethod]
    public void NativeSeedEncoding_IsExplicitSignedInt32LittleEndian()
    {
        AssertEncoding(0, 0x00, 0x00, 0x00, 0x00);
        AssertEncoding(1, 0x01, 0x00, 0x00, 0x00);
        AssertEncoding(-1, 0xff, 0xff, 0xff, 0xff);
        AssertEncoding(int.MinValue, 0x00, 0x00, 0x00, 0x80);
        AssertEncoding(int.MaxValue, 0xff, 0xff, 0xff, 0x7f);

        Assert.AreEqual(int.MinValue, NativeSeedEncoding.ToNativeSeedChecked(int.MinValue));
        Assert.AreEqual(int.MaxValue, NativeSeedEncoding.ToNativeSeedChecked(int.MaxValue));
        Assert.ThrowsExactly<OverflowException>(() => NativeSeedEncoding.ToNativeSeedChecked(int.MinValue - 1L));
        Assert.ThrowsExactly<OverflowException>(() => NativeSeedEncoding.ToNativeSeedChecked(int.MaxValue + 1L));
    }

    [TestMethod]
    public void NativeSeedEncoding_RejectsUndersizedBuffers()
    {
        Assert.ThrowsExactly<ArgumentException>(() => NativeSeedEncoding.Write(0, new byte[3]));
        Assert.ThrowsExactly<ArgumentException>(() => NativeSeedEncoding.Read(new byte[3]));
    }

    [TestMethod]
    [DataRow(0, 0x2084b3dacda37d42UL, 0xe1e679b22854472bUL)]
    [DataRow(1, 0xbc5a11932bd4a3b0UL, 0x3650290aee03c904UL)]
    [DataRow(-1, 0xf3e1ae6053fec50cUL, 0xb910bf9d460601faUL)]
    [DataRow(int.MinValue, 0xb9343f66ed9d8382UL, 0xa9b4f8d956667ffbUL)]
    [DataRow(int.MaxValue, 0x645f6efa5c583aaeUL, 0xb09617a2adf41995UL)]
    public void StatelessRandomV1_MatchesFrozenReferenceVectors(int seed, ulong expected0, ulong expected1)
    {
        Assert.AreEqual(expected0, StatelessRandomV1.NextUInt64(seed, RandomDomain.Hydrology, ReferenceStream, 0));
        Assert.AreEqual(expected1, StatelessRandomV1.NextUInt64(seed, RandomDomain.Hydrology, ReferenceStream, 1));
    }

    [TestMethod]
    public void DomainKeys_AreDistinctAndFrozen()
    {
        const int seed = 20_260_906;
        const ulong counter = 7;
        var expected = new Dictionary<RandomDomain, ulong>
        {
            [RandomDomain.Geology] = 0x6a89efa6f6d2fd78UL,
            [RandomDomain.Hydrology] = 0xb9eef5aa7fa08b54UL,
            [RandomDomain.Climate] = 0xfa276283353b0792UL,
            [RandomDomain.Vegetation] = 0x10c0761cb274708cUL,
            [RandomDomain.Caverns] = 0x439afdef84a034adUL,
            [RandomDomain.Sites] = 0xcc9f152e26c6a0acUL,
        };

        foreach ((RandomDomain domain, ulong expectedValue) in expected)
        {
            Assert.AreEqual(
                expectedValue,
                StatelessRandomV1.NextUInt64(seed, domain, ReferenceStream, counter),
                $"La clé figée du domaine {domain} a changé.");
        }

        Assert.HasCount(expected.Count, expected.Values.Distinct());
    }

    [TestMethod]
    public void UnknownDomainKeys_AreRejected()
    {
        const RandomDomain unknown = (RandomDomain)0xdeadbeefU;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            StatelessRandomV1.NextUInt64(0, unknown, ReferenceStream, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            StableId.Derive(unknown, ReferenceStream, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new StableIdSource(unknown, ReferenceStream, 0));
    }

    [TestMethod]
    public void ExplicitCounters_MakeEvaluationOrderAndVegetationConsumptionIrrelevant()
    {
        const int seed = -437_287_116;
        ulong[] counters = [0, 1, 2, 3, 100, ulong.MaxValue];

        var forwardHydrology = counters.ToDictionary(
            counter => counter,
            counter => StatelessRandomV1.NextUInt64(seed, RandomDomain.Hydrology, ReferenceStream, counter));
        var forwardCaverns = counters.ToDictionary(
            counter => counter,
            counter => StatelessRandomV1.NextUInt64(seed, RandomDomain.Caverns, ReferenceStream, counter));

        foreach (ulong counter in counters.Reverse())
        {
            _ = StatelessRandomV1.NextUInt64(seed, RandomDomain.Vegetation, ReferenceStream, counter);
            _ = StatelessRandomV1.NextUInt64(seed, RandomDomain.Vegetation, ReferenceStream, unchecked(counter + 10_000));

            Assert.AreEqual(
                forwardCaverns[counter],
                StatelessRandomV1.NextUInt64(seed, RandomDomain.Caverns, ReferenceStream, counter));
            Assert.AreEqual(
                forwardHydrology[counter],
                StatelessRandomV1.NextUInt64(seed, RandomDomain.Hydrology, ReferenceStream, counter));
        }
    }

    [TestMethod]
    public void StableId_UsesCanonical128BitEncodingAndFrozenVectors()
    {
        Assert.AreEqual(
            "fdbd12fc4d6da0761fae3b03544878b6",
            StableId.Derive(RandomDomain.Hydrology, ReferenceStream, 0).ToString());
        Assert.AreEqual(
            "1603f5d34ab247157dbc554ff79268f8",
            StableId.Derive(RandomDomain.Sites, StableId.Zero, ulong.MaxValue).ToString());
        Assert.AreEqual(
            "c74e8e133849b98a499dd266fd68fd74",
            StableId.Derive(RandomDomain.Geology, StableId.Zero, 42).ToString());

        Span<byte> canonicalBytes = stackalloc byte[StableId.ByteWidth];
        ReferenceStream.WriteCanonicalBytes(canonicalBytes);
        CollectionAssert.AreEqual(
            Convert.FromHexString("00112233445566778899aabbccddeeff"),
            canonicalBytes.ToArray());
        Assert.AreEqual(ReferenceStream, StableId.Parse(ReferenceStream.ToString()));
    }

    [TestMethod]
    public void StableId_HasNoCollisionInDocumented300000EntryCorpus()
    {
        const int indexesPerDomain = 50_000;
        var ids = new HashSet<StableId>(capacity: indexesPerDomain * RandomDomainCatalog.All.Count);
        var guard = new StableIdCollisionGuard();

        foreach (RandomDomain domain in RandomDomainCatalog.All)
        {
            for (ulong localIndex = 0; localIndex < indexesPerDomain; localIndex++)
            {
                var source = new StableIdSource(domain, StableId.Zero, localIndex);
                StableId id = guard.Register(source);
                Assert.IsTrue(ids.Add(id), $"Collision inattendue pour {domain}/{localIndex}.");
            }
        }

        Assert.HasCount(300_000, ids);
        Assert.AreEqual(ids.Count, guard.Count);
    }

    [TestMethod]
    public void CollisionGuard_IsIdempotentForTheSameCanonicalSource()
    {
        var guard = new StableIdCollisionGuard();
        var source = new StableIdSource(RandomDomain.Sites, ReferenceStream, 73);

        StableId first = guard.Register(source);
        StableId second = guard.Register(source);

        Assert.AreEqual(first, second);
        Assert.AreEqual(1, guard.Count);
    }

    [TestMethod]
    public void StableIdParser_RejectsNonCanonicalInputs()
    {
        Assert.ThrowsExactly<FormatException>(() => StableId.Parse("0011"));
        Assert.ThrowsExactly<FormatException>(() => StableId.Parse("00112233445566778899AABBCCDDEEFF"));
        Assert.ThrowsExactly<FormatException>(() => StableId.Parse("g0112233445566778899aabbccddeeff"));
    }

    private static void AssertEncoding(int seed, params byte[] expected)
    {
        Span<byte> encoded = stackalloc byte[NativeSeedEncoding.ByteWidth];
        NativeSeedEncoding.Write(seed, encoded);
        CollectionAssert.AreEqual(expected, encoded.ToArray());
        Assert.AreEqual(seed, NativeSeedEncoding.Read(encoded));
    }
}
