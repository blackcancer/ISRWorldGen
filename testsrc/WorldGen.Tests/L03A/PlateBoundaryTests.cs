using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03A;

[TestClass]
public sealed class PlateBoundaryTests
{
    [TestMethod]
    public void F05_CollisionDivergenceAndShearHaveDistinctFiniteFields()
    {
        PlateKinematics plateA = Plate(0, 1, 0);
        PlateKinematics collisionB = Plate(1, -1, 0);
        PlateKinematics divergenceA = Plate(0, -1, 0);
        PlateKinematics divergenceB = Plate(1, 1, 0);
        PlateKinematics shearA = Plate(0, 0, 1);
        PlateKinematics shearB = Plate(1, 0, -1);
        UnitDirection2 normalAtoB = UnitDirection2.FromComponents(1, 0);

        PlateBoundaryEffect collision = PlateBoundaryEvaluator.Evaluate(plateA, collisionB, normalAtoB);
        PlateBoundaryEffect divergence = PlateBoundaryEvaluator.Evaluate(divergenceA, divergenceB, normalAtoB);
        PlateBoundaryEffect shear = PlateBoundaryEvaluator.Evaluate(shearA, shearB, normalAtoB);

        Assert.AreEqual(PlateBoundaryKind.Collision, collision.Kind);
        Assert.AreEqual(PlateBoundaryKind.Divergence, divergence.Kind);
        Assert.AreEqual(PlateBoundaryKind.Shear, shear.Kind);
        Assert.IsGreaterThan(0, collision.UpliftNormalized);
        Assert.AreEqual(0, collision.SubsidenceNormalized);
        Assert.IsGreaterThan(0, divergence.SubsidenceNormalized);
        Assert.AreEqual(0, divergence.UpliftNormalized);
        Assert.IsGreaterThan(0, shear.ShearNormalized);
        Assert.AreEqual(0, shear.UpliftNormalized);
        Assert.AreNotEqual(collision.ContentChecksum, shear.ContentChecksum);

        foreach (double value in Values(collision).Concat(Values(divergence)).Concat(Values(shear)))
        {
            Assert.IsTrue(double.IsFinite(value));
            Assert.IsGreaterThanOrEqualTo(0, value);
            Assert.IsLessThanOrEqualTo(1, value);
        }
    }

    [TestMethod]
    public void SwappingLabelsAndReversingNormalPreservesConventionExactly()
    {
        PlateKinematics plateA = Plate(11, 0.75, 0.25);
        PlateKinematics plateB = Plate(12, -0.5, -0.4);
        UnitDirection2 normal = UnitDirection2.FromComponents(3, 4);

        PlateBoundaryEffect forward = PlateBoundaryEvaluator.Evaluate(plateA, plateB, normal);
        PlateBoundaryEffect reversed = PlateBoundaryEvaluator.Evaluate(plateB, plateA, normal.Negated());

        Assert.AreEqual(forward.Kind, reversed.Kind);
        Assert.AreEqual(forward.NormalRelativeVelocity, reversed.NormalRelativeVelocity, 1e-15);
        Assert.AreEqual(forward.TangentialRelativeSpeed, reversed.TangentialRelativeSpeed, 1e-15);
        Assert.AreEqual(forward.UpliftNormalized, reversed.UpliftNormalized, 1e-15);
        Assert.AreEqual(forward.SubsidenceNormalized, reversed.SubsidenceNormalized, 1e-15);
        Assert.AreEqual(forward.ShearNormalized, reversed.ShearNormalized, 1e-15);
        Assert.AreEqual(forward.ContentChecksum, reversed.ContentChecksum);
    }

    [TestMethod]
    public void PlateDomainCanContainSeveralCrustTypesWithoutChangingPlateIdentity()
    {
        StableId plateId = L03ATestSupport.PlateId(20);
        var domain = new PlateDomain(
            plateId,
            new PlateVelocity(0.25, -0.5),
            [
                new PlateCrustPatch(L03ATestSupport.PlateId(21), CrustKind.Continental, 250_000),
                new PlateCrustPatch(L03ATestSupport.PlateId(22), CrustKind.Transitional, 500_000),
                new PlateCrustPatch(L03ATestSupport.PlateId(23), CrustKind.Oceanic, 750_000),
            ]);

        Assert.AreEqual(plateId, domain.PlateId);
        CollectionAssert.AreEquivalent(
            new[] { CrustKind.Continental, CrustKind.Transitional, CrustKind.Oceanic },
            domain.CrustKinds.ToArray());
        Assert.IsTrue(domain.Patches.All(patch => patch.PlateId == plateId));
    }

    [TestMethod]
    public void InvalidOrUnboundedVectorsFailBeforePublishingAField()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlateVelocity(double.NaN, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlateVelocity(1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => UnitDirection2.FromComponents(0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => UnitDirection2.FromComponents(double.PositiveInfinity, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlateBoundaryEvaluator.Evaluate(
            Plate(0, 0, 0), Plate(1, 0, 0), default));
    }

    private static PlateKinematics Plate(ulong index, double x, double z) =>
        new(L03ATestSupport.PlateId(index), new PlateVelocity(x, z));

    private static IEnumerable<double> Values(PlateBoundaryEffect effect) =>
    [
        effect.IntensityNormalized,
        effect.UpliftNormalized,
        effect.SubsidenceNormalized,
        effect.ShearNormalized,
    ];
}
