using System.Buffers.Binary;
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
    private static readonly int[] TargetedFixtureSeeds =
        [20260907, -20260907, 731, -731, 196883, -196883, 48731, -48731];
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
    public void OldMassifTargetedPureCoresHaveMeasurableMultiSummitRelief()
    {
        int measuredViews = 0;
        var failures = new List<string>();
        var surfaceHashes = new HashSet<Hash256>();
        foreach (int seed in TargetedFixtureSeeds)
        {
            (LandscapeModel model, AtlasMesh atlas) = Build(seed);
            L03BPureLandscapeView? view = L03BEvidenceViews.TrySelectPureView(
                model, atlas, LandscapeFamily.OldMassifs, seed, L03BEvidenceViews.BlindMapSide);
            if (view is null)
            {
                continue;
            }

            measuredViews++;
            double[] altitudes = L03BEvidenceViews.Altitudes(view).Cast<double>().ToArray();
            double mean = altitudes.Average();
            double variance = altitudes.Select(value => (value - mean) * (value - mean)).Average();
            L03BMorphologyMeasurement morphology = L03BEvidenceViews.MeasureMorphology(view);
            int saturatedSamples = altitudes.Count(value => Math.Abs(value) >= .999d);
            surfaceHashes.Add(SurfaceHash(altitudes));

            Console.WriteLine(
                $"OldMassifs seed={seed}, owner={view.OwnerCellId}, span={view.SpanBlocks:R}, variance={variance:R}, peaks={morphology.ProminentPeaks}, valleys={morphology.ProminentValleys}, anisotropy={morphology.GradientAnisotropy:R}, saturated={saturatedSamples}");
            // This is the pre-declared campaign alarm, reproduced analytically on
            // owner-pure views without running or changing the sealed campaign.
            if (variance <= 0.0001d)
            {
                failures.Add($"seed {seed}: variance {variance:R} on {view.SpanBlocks:R} blocks");
            }
            if (morphology.ProminentPeaks < 2)
            {
                failures.Add($"seed {seed}: {morphology.ProminentPeaks} prominent summit(s)");
            }
            if (morphology.ProminentValleys < 1)
            {
                failures.Add($"seed {seed}: {morphology.ProminentValleys} prominent valley(s)");
            }
            if (saturatedSamples != 0)
            {
                failures.Add($"seed {seed}: {saturatedSamples} saturated sample(s)");
            }
        }

        Assert.AreEqual(TargetedFixtureSeeds.Length, measuredViews,
            "Every declared targeted seed must contain an OldMassifs pure-core view.");
        Assert.HasCount(measuredViews, surfaceHashes,
            "Centering the massif system must not homogenize its deterministic per-region variants.");
        Assert.HasCount(0, failures,
            "Every available OldMassifs pure core must preserve measurable multi-summit relief: " + string.Join("; ", failures));
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

    private static Hash256 SurfaceHash(IReadOnlyList<double> altitudes)
    {
        byte[] canonical = new byte[checked(altitudes.Count * sizeof(long))];
        for (int index = 0; index < altitudes.Count; index++)
        {
            BinaryPrimitives.WriteInt64BigEndian(
                canonical.AsSpan(index * sizeof(long), sizeof(long)),
                BitConverter.DoubleToInt64Bits(altitudes[index]));
        }
        return Hash256.Compute(canonical);
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
