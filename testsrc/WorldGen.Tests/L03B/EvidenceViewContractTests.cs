using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class EvidenceViewContractTests
{
    private const int FixtureSeed = 20260907;
    private static readonly Lazy<L03BPureLandscapeView> PureRangeView = new(BuildPureRangeView);

    [TestMethod]
    public void BlindMapsAndMorphologyMeasurementsAcceptOnlyPureOwnerPixels()
    {
        (LandscapeModel model, AtlasMesh atlas) = Build(FixtureSeed);
        foreach (LandscapeFamily family in Enum.GetValues<LandscapeFamily>())
        {
            L03BPureLandscapeView view = L03BEvidenceViews.SelectPureView(
                model, atlas, family, FixtureSeed, L03BEvidenceViews.BlindMapSide);
            L03BEvidenceViews.RequirePure(view);

            Assert.AreEqual(L03BEvidenceViews.BlindMapSide, view.Side);
            Assert.HasCount(view.Side * view.Side, view.Pixels);
            Assert.AreEqual(view.SpanBlocks / (view.Side - 1), view.StepBlocks, 1e-12);
            Assert.IsGreaterThanOrEqualTo(
                LandscapeFamilyCatalog.Get(family).MacroWavelengthBlocks * L03BEvidenceViews.MinimumMacroSpanFactor,
                view.SpanBlocks);
            Assert.IsTrue(view.Pixels.All(pixel =>
                pixel.Sample.DominantCellId == view.OwnerCellId &&
                pixel.Sample.DominantFamily == family &&
                !pixel.Sample.IsTransition &&
                pixel.Sample.ActiveResidualContributorCount == 1 &&
                pixel.Sample.PrimaryResidualWeight == 1d &&
                pixel.Sample.ForeignResidualWeight == 0d &&
                pixel.Sample.ForeignResidualContributionNormalized == 0d));

            double[,] altitudes = L03BEvidenceViews.Altitudes(view);
            Assert.AreEqual(view.Side, altitudes.GetLength(0));
            Assert.AreEqual(view.Side, altitudes.GetLength(1));
            Assert.IsTrue(altitudes.Cast<double>().All(double.IsFinite));
            L03BPureLandscapeView corpusView = L03BEvidenceViews.SelectPureView(
                model, atlas, family, FixtureSeed, L03BEvidenceViews.CorpusMapSide);
            L03BMorphologyMeasurement morphology = L03BEvidenceViews.MeasureMorphology(corpusView);
            Assert.IsTrue(new[]
            {
                morphology.Span,
                morphology.StepBlocks,
                morphology.MedianGradient,
                morphology.MedianCurvature,
                morphology.Curvature90,
                morphology.GradientAnisotropy,
                morphology.BroadRimLift,
                morphology.CraterLift,
                morphology.ConeDrop,
                morphology.HighFlatFraction,
            }.All(double.IsFinite));

            byte[] mask = L03BEvidenceViews.RenderTransitionMask(view);
            int payloadOffset = Encoding.ASCII.GetByteCount($"P5\n{view.Side} {view.Side}\n255\n");
            Assert.AreEqual(view.Side * view.Side, mask.Length - payloadOffset);
            Assert.IsTrue(mask.AsSpan(payloadOffset).ToArray().All(value => value == 0),
                "Every published transition-mask pixel must explicitly mark owner-pure data.");
        }
    }

    [TestMethod]
    [DataRow("owner")]
    [DataRow("family")]
    [DataRow("is-transition")]
    [DataRow("contributor-count")]
    [DataRow("primary-weight")]
    [DataRow("foreign-weight")]
    [DataRow("foreign-contribution")]
    public void EachIndependentPurityGuardRejectsRenderingAndMeasurement(string guard)
    {
        L03BPureLandscapeView pure = PureRangeView.Value;
        L03BEvidencePixel[] pixels = pure.Pixels.ToArray();
        LandscapeSample original = pixels[0].Sample;
        pixels[0] = pixels[0] with
        {
            Sample = guard switch
            {
                "owner" => original with { DominantCellId = StableId.Zero },
                "family" => original with
                {
                    DominantFamily = original.DominantFamily == LandscapeFamily.RuggedRanges
                        ? LandscapeFamily.OldMassifs
                        : LandscapeFamily.RuggedRanges,
                },
                "is-transition" => original with { IsTransition = true },
                "contributor-count" => original with { ActiveResidualContributorCount = original.ActiveResidualContributorCount + 1 },
                "primary-weight" => original with { PrimaryResidualWeight = .75d },
                "foreign-weight" => original with { ForeignResidualWeight = .25d },
                "foreign-contribution" => original with { ForeignResidualContributionNormalized = .01d },
                _ => throw new AssertFailedException($"Unknown independent guard mutation {guard}."),
            },
        };
        L03BPureLandscapeView contaminated = pure with { Pixels = Array.AsReadOnly(pixels) };

        Assert.ThrowsExactly<InvalidDataException>(() => L03BEvidenceViews.RequirePure(contaminated));
        Assert.ThrowsExactly<InvalidDataException>(() => L03BEvidenceViews.Altitudes(contaminated));
        Assert.ThrowsExactly<InvalidDataException>(() => L03BEvidenceViews.RenderTransitionMask(contaminated));
        Assert.ThrowsExactly<InvalidDataException>(() => L03BEvidenceViews.MeasureMorphology(contaminated));
    }

    private static L03BPureLandscapeView BuildPureRangeView()
    {
        (LandscapeModel model, AtlasMesh atlas) = Build(FixtureSeed);
        L03BPureLandscapeView view = L03BEvidenceViews.SelectPureView(
            model, atlas, LandscapeFamily.RuggedRanges, FixtureSeed, L03BEvidenceViews.CorpusMapSide);
        Assert.AreNotEqual(StableId.Zero, view.OwnerCellId);
        return view;
    }

    private static (LandscapeModel Model, AtlasMesh Atlas) Build(int seed)
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("vast-expeditions");
        GenerationIdentity identity = L03BTestSupport.Identity(seed, profile);
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(seed, profile);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(
            identity,
            atlas,
            plates,
            profile,
            new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25)));
        return (model, atlas);
    }
}
