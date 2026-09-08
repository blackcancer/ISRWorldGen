using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Materials;

namespace ISRWorldGen.Tests.L03C;

[TestClass]
public sealed class MaterialSnapshotTests
{
    [TestMethod]
    public void T03_03_SedimentaryVolcanicPropertiesAreBoundedCompleteAndFracturesReachConsumers()
    {
        MaterialSnapshot snapshot = SyntheticSnapshot();

        MaterialSample sandstone = snapshot.Query(100, 70, 100);
        MaterialSample limestoneFracture = snapshot.Query(0, 45, 1);
        MaterialSample basalt = snapshot.Query(100, 10, 100);

        Assert.AreEqual(GeologicalMaterialCode.Sandstone, sandstone.MaterialCode);
        Assert.AreEqual(GeologicalMaterialCode.Limestone, limestoneFracture.MaterialCode);
        Assert.AreEqual(GeologicalMaterialCode.Basalt, basalt.MaterialCode);
        Assert.IsTrue(limestoneFracture.IsFractured);
        Assert.AreEqual(0.9, limestoneFracture.Properties.PermeabilityNormalized, 1e-15);
        foreach (MaterialProperties properties in snapshot.Catalog.Properties.Values)
        {
            Assert.IsTrue(double.IsFinite(properties.ErosionResistanceNormalized));
            Assert.IsTrue(double.IsFinite(properties.SolubilityNormalized));
            Assert.IsTrue(double.IsFinite(properties.PermeabilityNormalized));
            Assert.IsGreaterThanOrEqualTo(0d, properties.ErosionResistanceNormalized);
            Assert.IsLessThanOrEqualTo(1d, properties.ErosionResistanceNormalized);
            Assert.IsGreaterThanOrEqualTo(0d, properties.SolubilityNormalized);
            Assert.IsLessThanOrEqualTo(1d, properties.SolubilityNormalized);
            Assert.IsGreaterThanOrEqualTo(0d, properties.PermeabilityNormalized);
            Assert.IsLessThanOrEqualTo(1d, properties.PermeabilityNormalized);
        }
    }

    [TestMethod]
    public void T03_04_ExcavationRevealsExistingStrataAndChunkBoundariesDoNotReassignThem()
    {
        MaterialSnapshot snapshot = SyntheticSnapshot();

        MaterialSample beforeExcavation = snapshot.Query(15, 70, 15);
        MaterialSample exposedAfterRemovingOverburden = snapshot.Query(15, 45, 15);
        MaterialSample sameExposedPointFromNextChunk = snapshot.Query(16, 45, 15);
        MaterialSample replay = snapshot.Query(15, 45, 15);

        Assert.AreEqual(GeologicalMaterialCode.Sandstone, beforeExcavation.MaterialCode);
        Assert.AreEqual(GeologicalMaterialCode.Limestone, exposedAfterRemovingOverburden.MaterialCode);
        Assert.AreEqual(exposedAfterRemovingOverburden, replay);
        Assert.AreEqual(exposedAfterRemovingOverburden.MaterialCode, sameExposedPointFromNextChunk.MaterialCode);
        Assert.AreEqual(exposedAfterRemovingOverburden.LayerId, sameExposedPointFromNextChunk.LayerId);
    }

    [TestMethod]
    public void InvalidGapsAndOutOfRangeQueriesAreRejectedBeforeMaterialPublication()
    {
        MaterialCatalog catalog = Catalog();
        Assert.ThrowsExactly<ArgumentException>(() => new MaterialSnapshot(catalog,
        [
            Layer(0, 0, 20, GeologicalMaterialCode.Basalt),
            Layer(1, 21, 40, GeologicalMaterialCode.Limestone),
        ], []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SyntheticSnapshot().Query(0, 100, 0));
    }

    private static MaterialSnapshot SyntheticSnapshot() => new(
        Catalog(),
        [
            Layer(0, 0, 20, GeologicalMaterialCode.Basalt),
            Layer(1, 20, 60, GeologicalMaterialCode.Limestone),
            Layer(2, 60, 90, GeologicalMaterialCode.Sandstone),
        ],
        [new FractureZone(Id(10), 0, 0, 1, 0, 2, 0.9)]);

    private static MaterialCatalog Catalog() => new(
    [
        Pair(GeologicalMaterialCode.Sandstone, 0.45, 0.1, 0.55),
        Pair(GeologicalMaterialCode.Shale, 0.25, 0.05, 0.2),
        Pair(GeologicalMaterialCode.Limestone, 0.6, 0.95, 0.35),
        Pair(GeologicalMaterialCode.Basalt, 0.9, 0.02, 0.08),
        Pair(GeologicalMaterialCode.Granite, 0.85, 0.01, 0.04),
    ]);

    private static KeyValuePair<GeologicalMaterialCode, MaterialProperties> Pair(GeologicalMaterialCode code, double erosion, double solubility, double permeability) => new(code, new MaterialProperties(erosion, solubility, permeability));

    private static StratigraphicLayer Layer(ulong id, int bottom, int top, GeologicalMaterialCode code) => new(Id(id), bottom, top, code);

    private static StableId Id(ulong index) => StableId.Derive(RandomDomain.Geology, StableId.Zero, index + 50_000);
}
