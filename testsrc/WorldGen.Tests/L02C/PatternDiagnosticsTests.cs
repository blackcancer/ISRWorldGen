using System.Collections;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Tests.L02C;

[TestClass]
public sealed class PatternDiagnosticsTests
{
    [TestMethod]
    public void DeliberatelyVisibleVoronoiWitness_IsSignalledByAlignmentAndPeriodicityDiagnostics()
    {
        PatternInspectionMaps witness = PatternTestSupport.VisibleVoronoiWitness();
        PatternDiagnosticPolicy policy = PatternTestSupport.CalibratedPolicy();

        PatternDiagnosticReport report = ProfileTestSupport.Success(
            PatternDiagnostics.Analyze(witness, policy));

        Assert.IsTrue(report.SignalDetected);
        Assert.IsTrue(report.EdgeAlignmentExceeded);
        Assert.IsTrue(report.PeriodicityExceeded);
        Assert.IsGreaterThanOrEqualTo(policy.EdgeAlignmentThresholdPpm, report.EdgeAlignmentScorePpm);
        Assert.IsGreaterThanOrEqualTo(policy.PeriodicityThresholdPpm, report.AxisPeriodicityScorePpm);
        Assert.IsTrue(report.StrongestHorizontalLag is 8 or 16);
        Assert.IsTrue(report.StrongestVerticalLag is 8 or 16);
        Assert.IsTrue(report.RequiresQualitativeReview);
    }

    [TestMethod]
    public void SmoothNonPeriodicField_IsNotPromotedToUniversalVisualPass()
    {
        PatternDiagnosticReport report = ProfileTestSupport.Success(PatternDiagnostics.Analyze(
            PatternTestSupport.SmoothField(),
            PatternTestSupport.CalibratedPolicy()));

        Assert.IsFalse(report.SignalDetected);
        Assert.IsFalse(report.EdgeAlignmentExceeded);
        Assert.IsFalse(report.PeriodicityExceeded);
        Assert.IsTrue(report.RequiresQualitativeReview);
        Assert.AreEqual(PatternReviewDisposition.QualitativeReviewRequired, report.ReviewDisposition);
    }

    [TestMethod]
    public void SensitivityReport_RecordsFalsePositivesFalseNegativesAndLimitsInStableOrder()
    {
        PatternDiagnosticPolicy policy = PatternTestSupport.CalibratedPolicy();
        PatternCorpusCase[] corpus =
        [
            new("04-low-contrast-voronoi", expectedVisibleVoronoi: true, PatternTestSupport.LowContrastWitness()),
            new("02-smooth", expectedVisibleVoronoi: false, PatternTestSupport.SmoothField()),
            new("03-periodic-non-voronoi", expectedVisibleVoronoi: false, PatternTestSupport.PeriodicNonVoronoi()),
            new("01-visible-voronoi", expectedVisibleVoronoi: true, PatternTestSupport.VisibleVoronoiWitness()),
        ];

        PatternSensitivityReport report = ProfileTestSupport.Success(
            PatternSensitivityEvaluator.Evaluate(corpus, policy));
        PatternSensitivityReport reversed = ProfileTestSupport.Success(
            PatternSensitivityEvaluator.Evaluate(corpus.Reverse().ToArray(), policy));

        Assert.AreEqual(1, report.TruePositiveCount);
        Assert.AreEqual(1, report.TrueNegativeCount);
        Assert.AreEqual(1, report.FalsePositiveCount);
        Assert.AreEqual(1, report.FalseNegativeCount);
        Assert.AreEqual(500_000, report.SensitivityPpm);
        Assert.AreEqual(500_000, report.SpecificityPpm);
        CollectionAssert.AreEqual(
            corpus.Select(item => item.CaseId).Order(StringComparer.Ordinal).ToArray(),
            report.Cases.Select(item => item.CaseId).ToArray());
        Assert.AreEqual(report.ContentChecksum, reversed.ContentChecksum);
        Assert.IsGreaterThanOrEqualTo(3, report.Limitations.Count);
        Assert.IsTrue(report.Limitations.Any(limit => limit.Contains("not universal", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(report.RequiresQualitativeReview);
    }

    [TestMethod]
    public void InspectionMaps_AreSeparateDefensiveCopiesWithCanonicalChecksum()
    {
        ushort[] final = PatternTestSupport.CreateVisibleFinalValues();
        ushort[] edges = PatternTestSupport.CreateGridEdges();
        PatternInspectionMaps maps = PatternTestSupport.Maps(final, edges);
        Hash256 checksum = maps.ContentChecksum;
        final[0] = ushort.MaxValue;
        edges[0] = ushort.MaxValue;

        Assert.AreEqual(checksum, maps.ContentChecksum);
        Assert.AreNotSame(maps.FinalOutput, maps.AtlasEdges);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<ushort>)maps.FinalOutput.Values)[0] = 1);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<ushort>)maps.AtlasEdges.Values)[0] = 1);
        CollectionAssert.AreNotEqual(maps.FinalOutput.Values.ToArray(), maps.AtlasEdges.Values.ToArray());
    }

    [TestMethod]
    public void WorkerCountClockAndPathsCannotEnterMapOrDiagnosticChecksums()
    {
        string[] forbiddenTokens = ["Worker", "Clock", "Time", "Path", "Account"];
        Type[] geographicTypes =
        [
            typeof(InspectionRaster),
            typeof(PatternInspectionMaps),
            typeof(PatternDiagnosticReport),
            typeof(PatternSensitivityReport),
        ];

        foreach (Type type in geographicTypes)
        {
            Assert.IsFalse(type.GetProperties().Any(property =>
                forbiddenTokens.Any(token => property.Name.Contains(token, StringComparison.OrdinalIgnoreCase))),
                $"{type.Name} exposes non-geographic runtime metadata.");
        }
    }

    [TestMethod]
    public void MapsWithDifferentDimensions_AreTypedFailure()
    {
        InspectionRaster final = PatternTestSupport.Raster(4, 4, new ushort[16]);
        InspectionRaster edges = PatternTestSupport.Raster(8, 2, new ushort[16]);

        GenerationFailure<PatternInspectionMaps> failure = ProfileTestSupport.Failure(
            PatternInspectionMaps.Create(final, edges));

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.pattern.map-dimensions", failure.Error.Stage);
    }

    [TestMethod]
    public void ImpossibleRasterCapacity_IsRefusedBeforeEnumerationOrAllocation()
    {
        var values = new ImpossibleRasterList(int.MaxValue);

        GenerationFailure<InspectionRaster> failure = ProfileTestSupport.Failure(
            InspectionRaster.Create(int.MaxValue, int.MaxValue, values));

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.pattern.raster-capacity", failure.Error.Stage);
        Assert.AreEqual(0, values.EnumerationCount);
    }

    [TestMethod]
    public void DiagnosticInputsAndOutputsRejectUnknownOrInvalidPolicies()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PatternDiagnosticPolicy(
            "invalid",
            policyVersion: 1,
            edgeGradientThreshold: 1,
            edgeAlignmentThresholdPpm: 1_000_001,
            periodicityThresholdPpm: 1,
            minimumPeriodLag: 2,
            maximumPeriodLag: 8));

        PatternInspectionMaps maps = PatternTestSupport.VisibleVoronoiWitness();
        PatternDiagnosticPolicy tooLarge = new(
            "unsupported-raster",
            policyVersion: 1,
            edgeGradientThreshold: 1,
            edgeAlignmentThresholdPpm: 1,
            periodicityThresholdPpm: 1,
            minimumPeriodLag: 2,
            maximumPeriodLag: 64);
        GenerationFailure<PatternDiagnosticReport> failure = ProfileTestSupport.Failure(
            PatternDiagnostics.Analyze(maps, tooLarge));
        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.pattern.policy-range", failure.Error.Stage);
    }

    [TestMethod]
    public void QualitativeEvidence_WritesSeparateFinalEdgeMapsAndSensitivityReport()
    {
        string repositoryRoot = PatternTestSupport.FindRepositoryRoot();
        string outputDirectory = Path.Combine(repositoryRoot, ".local", "L02C", "qualitative-evidence");
        Directory.CreateDirectory(outputDirectory);
        PatternInspectionMaps witness = PatternTestSupport.VisibleVoronoiWitness();
        PatternDiagnosticPolicy policy = PatternTestSupport.CalibratedPolicy();
        PatternDiagnosticReport diagnostic = ProfileTestSupport.Success(PatternDiagnostics.Analyze(witness, policy));
        PatternSensitivityReport sensitivity = ProfileTestSupport.Success(PatternSensitivityEvaluator.Evaluate(
        [
            new PatternCorpusCase("visible-voronoi", true, witness),
            new PatternCorpusCase("smooth", false, PatternTestSupport.SmoothField()),
            new PatternCorpusCase("periodic-non-voronoi", false, PatternTestSupport.PeriodicNonVoronoi()),
            new PatternCorpusCase("low-contrast-voronoi", true, PatternTestSupport.LowContrastWitness()),
        ], policy));

        string finalPath = Path.Combine(outputDirectory, "visible-voronoi-final.pgm");
        string edgesPath = Path.Combine(outputDirectory, "visible-voronoi-edges.pgm");
        string reportPath = Path.Combine(outputDirectory, "sensitivity-report.json");
        File.WriteAllBytes(finalPath, InspectionMapRenderer.RenderPortableGraymap(witness.FinalOutput));
        File.WriteAllBytes(edgesPath, InspectionMapRenderer.RenderPortableGraymap(witness.AtlasEdges));
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            status = "REVIEW_REQUIRED",
            requirement = "R02-06",
            test = "T02-06",
            detectorPolicy = policy.PolicyId,
            detectorPolicyVersion = policy.PolicyVersion,
            mapsChecksum = witness.ContentChecksum.ToString(),
            diagnosticChecksum = diagnostic.ContentChecksum.ToString(),
            sensitivityChecksum = sensitivity.ContentChecksum.ToString(),
            edgeAlignmentScorePpm = diagnostic.EdgeAlignmentScorePpm,
            axisPeriodicityScorePpm = diagnostic.AxisPeriodicityScorePpm,
            truePositiveCount = sensitivity.TruePositiveCount,
            trueNegativeCount = sensitivity.TrueNegativeCount,
            falsePositiveCount = sensitivity.FalsePositiveCount,
            falseNegativeCount = sensitivity.FalseNegativeCount,
            limitations = sensitivity.Limitations,
            evidenceMaps = new[] { Path.GetFileName(finalPath), Path.GetFileName(edgesPath) },
        }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.IsTrue(File.Exists(finalPath));
        Assert.IsTrue(File.Exists(edgesPath));
        Assert.IsTrue(File.Exists(reportPath));
        CollectionAssert.AreNotEqual(File.ReadAllBytes(finalPath), File.ReadAllBytes(edgesPath));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        Assert.AreEqual("REVIEW_REQUIRED", document.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(1, document.RootElement.GetProperty("falsePositiveCount").GetInt32());
        Assert.AreEqual(1, document.RootElement.GetProperty("falseNegativeCount").GetInt32());
    }

    private sealed class ImpossibleRasterList : IReadOnlyList<ushort>
    {
        internal ImpossibleRasterList(int count) => Count = count;

        public int Count { get; }

        public ushort this[int index] => throw new NotSupportedException();

        public int EnumerationCount { get; private set; }

        public IEnumerator<ushort> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("Impossible raster must be rejected before enumeration.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
