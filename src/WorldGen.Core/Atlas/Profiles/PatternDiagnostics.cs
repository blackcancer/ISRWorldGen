using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.Profiles;

public sealed record PatternInspectionBudget
{
    public PatternInspectionBudget(
        long maximumPixelsPerRaster,
        long maximumBytesPerRaster,
        long maximumMapPairBytes)
    {
        if (maximumPixelsPerRaster <= 0 || maximumPixelsPerRaster >= Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPixelsPerRaster),
                maximumPixelsPerRaster,
                "Raster pixel budget must be positive and below CLR array capacity.");
        }

        if (maximumBytesPerRaster <= 0 || maximumMapPairBytes < maximumBytesPerRaster)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMapPairBytes),
                maximumMapPairBytes,
                "Map byte budgets must be positive and ordered.");
        }

        MaximumPixelsPerRaster = maximumPixelsPerRaster;
        MaximumBytesPerRaster = maximumBytesPerRaster;
        MaximumMapPairBytes = maximumMapPairBytes;
    }

    public long MaximumPixelsPerRaster { get; }

    public long MaximumBytesPerRaster { get; }

    public long MaximumMapPairBytes { get; }
}

public sealed class InspectionRaster
{
    private const long EstimatedArrayAndWrapperBytes = 56;

    private InspectionRaster(int width, int height, ushort[] values, long estimatedStorageBytes)
    {
        Width = width;
        Height = height;
        Values = Array.AsReadOnly(values);
        EstimatedStorageBytes = estimatedStorageBytes;
        ContentChecksum = PatternCanonical.ComputeRasterHash(width, height, values);
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyCollection<ushort> Values { get; }

    public long EstimatedStorageBytes { get; }

    public Hash256 ContentChecksum { get; }

    public ushort this[int x, int z]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)z >= (uint)Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Raster coordinate lies outside the finite map.");
            }

            return Values[checked((z * Width) + x)];
        }
    }

    public static GenerationResult<InspectionRaster> Create(
        int width,
        int height,
        IReadOnlyList<ushort> values,
        PatternInspectionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(budget);
        BigInteger cellCount = (BigInteger)width * height;
        if (width <= 0 || height <= 0 || cellCount > Array.MaxLength)
        {
            return PatternFailure.For<InspectionRaster>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.raster-capacity",
                Hash256.Zero,
                "Raster dimensions are non-positive or exceed the runtime array capacity.");
        }

        BigInteger estimatedStorageBytes = EstimatedArrayAndWrapperBytes + (cellCount * sizeof(ushort));
        if (cellCount > budget.MaximumPixelsPerRaster || estimatedStorageBytes > budget.MaximumBytesPerRaster)
        {
            return PatternFailure.For<InspectionRaster>(
                GenerationFailureCode.BudgetExceeded,
                "atlas.pattern.raster-budget",
                Hash256.Zero,
                $"Raster requires {cellCount} pixels and {estimatedStorageBytes} estimated bytes, exceeding explicit inspection budget.");
        }

        int expectedCount = (int)cellCount;
        if (values.Count != expectedCount)
        {
            return PatternFailure.For<InspectionRaster>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.raster-length",
                Hash256.Zero,
                "Raster sample count does not match its checked dimensions.");
        }

        ushort[] copy = new ushort[expectedCount];
        for (int index = 0; index < copy.Length; index++)
        {
            copy[index] = values[index];
        }

        return GenerationResult<InspectionRaster>.Success(new InspectionRaster(
            width,
            height,
            copy,
            checked((long)estimatedStorageBytes)));
    }
}

public sealed class PatternInspectionMaps
{
    private PatternInspectionMaps(InspectionRaster finalOutput, InspectionRaster atlasEdges)
    {
        FinalOutput = finalOutput;
        AtlasEdges = atlasEdges;
        ContentChecksum = PatternCanonical.ComputeMapPairHash(finalOutput, atlasEdges);
    }

    public InspectionRaster FinalOutput { get; }

    public InspectionRaster AtlasEdges { get; }

    public Hash256 ContentChecksum { get; }

    public static GenerationResult<PatternInspectionMaps> Create(
        InspectionRaster finalOutput,
        InspectionRaster atlasEdges,
        PatternInspectionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(finalOutput);
        ArgumentNullException.ThrowIfNull(atlasEdges);
        ArgumentNullException.ThrowIfNull(budget);
        if (ReferenceEquals(finalOutput, atlasEdges))
        {
            return PatternFailure.For<PatternInspectionMaps>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.map-separation",
                finalOutput.ContentChecksum,
                "Final output and atlas edges require distinct immutable raster layers.");
        }

        if (finalOutput.Width != atlasEdges.Width || finalOutput.Height != atlasEdges.Height)
        {
            return PatternFailure.For<PatternInspectionMaps>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.map-dimensions",
                finalOutput.ContentChecksum,
                "Final output and atlas-edge maps require identical dimensions.");
        }

        BigInteger mapPairBytes = (BigInteger)finalOutput.EstimatedStorageBytes + atlasEdges.EstimatedStorageBytes;
        if (mapPairBytes > budget.MaximumMapPairBytes)
        {
            return PatternFailure.For<PatternInspectionMaps>(
                GenerationFailureCode.BudgetExceeded,
                "atlas.pattern.map-budget",
                finalOutput.ContentChecksum,
                $"Inspection map pair requires {mapPairBytes} estimated bytes, exceeding explicit pair budget.");
        }

        return GenerationResult<PatternInspectionMaps>.Success(new PatternInspectionMaps(finalOutput, atlasEdges));
    }
}

/// <summary>
/// A named calibration policy. Its thresholds qualify only the associated corpus and never constitute a universal visual oracle.
/// </summary>
public sealed record PatternDiagnosticPolicy
{
    public PatternDiagnosticPolicy(
        string policyId,
        uint policyVersion,
        int edgeGradientThreshold,
        int edgeAlignmentThresholdPpm,
        int periodicityThresholdPpm,
        int minimumPeriodLag,
        int maximumPeriodLag,
        long maximumAnalysisWorkUnits,
        long maximumCorpusAnalysisWorkUnits)
    {
        if (!ProfileCanonicalEncoding.IsCanonicalIdentifier(policyId))
        {
            throw new ArgumentException("Policy ID must be bounded canonical NFC text.", nameof(policyId));
        }

        if (policyVersion == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion), policyVersion, "Policy version must be positive.");
        }

        if (edgeGradientThreshold is < 1 or > 131_070)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeGradientThreshold), edgeGradientThreshold, "Gradient threshold must be in [1, 131070].");
        }

        if (edgeAlignmentThresholdPpm is < 0 or > PatternDiagnostics.PartsPerMillion ||
            periodicityThresholdPpm is < 0 or > PatternDiagnostics.PartsPerMillion)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeAlignmentThresholdPpm), "Diagnostic thresholds must be in [0, 1000000] ppm.");
        }

        if (minimumPeriodLag < 2 || maximumPeriodLag < minimumPeriodLag)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPeriodLag), "Period lag interval is invalid.");
        }

        if (maximumAnalysisWorkUnits <= 0 || maximumCorpusAnalysisWorkUnits < maximumAnalysisWorkUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCorpusAnalysisWorkUnits),
                maximumCorpusAnalysisWorkUnits,
                "Analysis work budgets must be positive and ordered.");
        }

        PolicyId = policyId;
        PolicyVersion = policyVersion;
        EdgeGradientThreshold = edgeGradientThreshold;
        EdgeAlignmentThresholdPpm = edgeAlignmentThresholdPpm;
        PeriodicityThresholdPpm = periodicityThresholdPpm;
        MinimumPeriodLag = minimumPeriodLag;
        MaximumPeriodLag = maximumPeriodLag;
        MaximumAnalysisWorkUnits = maximumAnalysisWorkUnits;
        MaximumCorpusAnalysisWorkUnits = maximumCorpusAnalysisWorkUnits;
        ContentChecksum = PatternCanonical.ComputePolicyHash(this);
    }

    public string PolicyId { get; }

    public uint PolicyVersion { get; }

    public int EdgeGradientThreshold { get; }

    public int EdgeAlignmentThresholdPpm { get; }

    public int PeriodicityThresholdPpm { get; }

    public int MinimumPeriodLag { get; }

    public int MaximumPeriodLag { get; }

    public long MaximumAnalysisWorkUnits { get; }

    public long MaximumCorpusAnalysisWorkUnits { get; }

    public Hash256 ContentChecksum { get; }
}

public enum PatternReviewDisposition
{
    QualitativeReviewRequired = 1,
}

public sealed class PatternDiagnosticReport
{
    internal PatternDiagnosticReport(
        string policyId,
        uint policyVersion,
        Hash256 policyChecksum,
        Hash256 mapsChecksum,
        int edgeAlignmentScorePpm,
        int axisPeriodicityScorePpm,
        int strongestHorizontalLag,
        int strongestVerticalLag,
        bool edgeAlignmentExceeded,
        bool periodicityExceeded,
        IEnumerable<string> findings)
    {
        PolicyId = policyId;
        PolicyVersion = policyVersion;
        PolicyChecksum = policyChecksum;
        MapsChecksum = mapsChecksum;
        EdgeAlignmentScorePpm = edgeAlignmentScorePpm;
        AxisPeriodicityScorePpm = axisPeriodicityScorePpm;
        StrongestHorizontalLag = strongestHorizontalLag;
        StrongestVerticalLag = strongestVerticalLag;
        EdgeAlignmentExceeded = edgeAlignmentExceeded;
        PeriodicityExceeded = periodicityExceeded;
        SignalDetected = edgeAlignmentExceeded || periodicityExceeded;
        RequiresQualitativeReview = true;
        ReviewDisposition = PatternReviewDisposition.QualitativeReviewRequired;
        Findings = Array.AsReadOnly(findings.ToArray());
        ContentChecksum = PatternCanonical.ComputeDiagnosticHash(this);
    }

    public string PolicyId { get; }

    public uint PolicyVersion { get; }

    public Hash256 PolicyChecksum { get; }

    public Hash256 MapsChecksum { get; }

    public int EdgeAlignmentScorePpm { get; }

    public int AxisPeriodicityScorePpm { get; }

    public int StrongestHorizontalLag { get; }

    public int StrongestVerticalLag { get; }

    public bool EdgeAlignmentExceeded { get; }

    public bool PeriodicityExceeded { get; }

    public bool SignalDetected { get; }

    public bool RequiresQualitativeReview { get; }

    public PatternReviewDisposition ReviewDisposition { get; }

    public ReadOnlyCollection<string> Findings { get; }

    public Hash256 ContentChecksum { get; }
}

public sealed class PatternAnalysisEstimate
{
    internal PatternAnalysisEstimate(long rasterPixels, int evaluatedLagCount, long workUnits)
    {
        RasterPixels = rasterPixels;
        EvaluatedLagCount = evaluatedLagCount;
        WorkUnits = workUnits;
    }

    public long RasterPixels { get; }

    public int EvaluatedLagCount { get; }

    public long WorkUnits { get; }
}

public static class PatternDiagnostics
{
    public const int PartsPerMillion = 1_000_000;

    public static GenerationResult<PatternAnalysisEstimate> Estimate(
        PatternInspectionMaps maps,
        PatternDiagnosticPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.MaximumPeriodLag >= maps.FinalOutput.Width ||
            policy.MaximumPeriodLag >= maps.FinalOutput.Height)
        {
            return PatternFailure.For<PatternAnalysisEstimate>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.policy-range",
                maps.ContentChecksum,
                "Diagnostic lag range does not fit both raster axes.");
        }

        int lagCount = checked(policy.MaximumPeriodLag - policy.MinimumPeriodLag + 1);
        BigInteger rasterPixels = (BigInteger)maps.FinalOutput.Width * maps.FinalOutput.Height;
        BigInteger workUnits = rasterPixels + (2 * rasterPixels * lagCount);
        if (workUnits > long.MaxValue)
        {
            return PatternFailure.For<PatternAnalysisEstimate>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.analysis-capacity",
                maps.ContentChecksum,
                "Diagnostic work cannot be represented by the qualified 64-bit model.");
        }

        if (workUnits > policy.MaximumAnalysisWorkUnits)
        {
            return PatternFailure.For<PatternAnalysisEstimate>(
                GenerationFailureCode.BudgetExceeded,
                "atlas.pattern.analysis-budget",
                maps.ContentChecksum,
                $"Diagnostic requires {workUnits} work units, exceeding explicit policy budget {policy.MaximumAnalysisWorkUnits}.");
        }

        return GenerationResult<PatternAnalysisEstimate>.Success(new PatternAnalysisEstimate(
            checked((long)rasterPixels),
            lagCount,
            checked((long)workUnits)));
    }

    public static GenerationResult<PatternDiagnosticReport> Analyze(
        PatternInspectionMaps maps,
        PatternDiagnosticPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(policy);
        GenerationResult<PatternAnalysisEstimate> estimateResult = Estimate(maps, policy);
        if (estimateResult is GenerationFailure<PatternAnalysisEstimate> estimateFailure)
        {
            return PatternFailure.For<PatternDiagnosticReport>(
                estimateFailure.Error.Code,
                estimateFailure.Error.Stage,
                estimateFailure.Error.InputHash,
                estimateFailure.Error.Details);
        }

        int edgeAlignment = ComputeEdgeAlignment(maps, policy.EdgeGradientThreshold);
        (int horizontalScore, int horizontalLag) = ComputeStrongestPeriodicity(
            maps.FinalOutput,
            policy,
            horizontal: true);
        (int verticalScore, int verticalLag) = ComputeStrongestPeriodicity(
            maps.FinalOutput,
            policy,
            horizontal: false);
        int periodicity = Math.Max(horizontalScore, verticalScore);
        bool edgeExceeded = edgeAlignment >= policy.EdgeAlignmentThresholdPpm;
        bool periodicityExceeded = periodicity >= policy.PeriodicityThresholdPpm;
        var findings = new List<string>(2);
        if (edgeExceeded)
        {
            findings.Add("final-field transitions align with the separate atlas-edge witness");
        }

        if (periodicityExceeded)
        {
            findings.Add("thresholded final-field transitions repeat along a raster axis");
        }

        return GenerationResult<PatternDiagnosticReport>.Success(new PatternDiagnosticReport(
            policy.PolicyId,
            policy.PolicyVersion,
            policy.ContentChecksum,
            maps.ContentChecksum,
            edgeAlignment,
            periodicity,
            horizontalLag,
            verticalLag,
            edgeExceeded,
            periodicityExceeded,
            findings));
    }

    private static int ComputeEdgeAlignment(PatternInspectionMaps maps, int gradientThreshold)
    {
        long edgePixels = 0;
        long nonEdgePixels = 0;
        long alignedEdges = 0;
        long alignedNonEdges = 0;
        for (int z = 1; z < maps.FinalOutput.Height; z++)
        {
            for (int x = 1; x < maps.FinalOutput.Width; x++)
            {
                bool transition = GradientAt(maps.FinalOutput, x, z) >= gradientThreshold;
                if (maps.AtlasEdges[x, z] != 0)
                {
                    edgePixels++;
                    if (transition)
                    {
                        alignedEdges++;
                    }
                }
                else
                {
                    nonEdgePixels++;
                    if (transition)
                    {
                        alignedNonEdges++;
                    }
                }
            }
        }

        if (edgePixels == 0 || nonEdgePixels == 0)
        {
            return 0;
        }

        BigInteger numerator = ((BigInteger)alignedEdges * nonEdgePixels) -
            ((BigInteger)alignedNonEdges * edgePixels);
        if (numerator <= BigInteger.Zero)
        {
            return 0;
        }

        BigInteger denominator = (BigInteger)edgePixels * nonEdgePixels;
        return checked((int)BigInteger.Min(PartsPerMillion, (numerator * PartsPerMillion) / denominator));
    }

    private static (int Score, int Lag) ComputeStrongestPeriodicity(
        InspectionRaster raster,
        PatternDiagnosticPolicy policy,
        bool horizontal)
    {
        int strongestScore = 0;
        int strongestLag = 0;
        for (int lag = policy.MinimumPeriodLag; lag <= policy.MaximumPeriodLag; lag++)
        {
            BigInteger count = BigInteger.Zero;
            BigInteger sumA = BigInteger.Zero;
            BigInteger sumB = BigInteger.Zero;
            BigInteger sumAB = BigInteger.Zero;
            BigInteger sumA2 = BigInteger.Zero;
            BigInteger sumB2 = BigInteger.Zero;
            int maxX = horizontal ? raster.Width - lag : raster.Width;
            int maxZ = horizontal ? raster.Height : raster.Height - lag;
            for (int z = 1; z < maxZ; z++)
            {
                for (int x = 1; x < maxX; x++)
                {
                    int a = GradientAt(raster, x, z) >= policy.EdgeGradientThreshold ? 1 : 0;
                    int b = horizontal
                        ? (GradientAt(raster, x + lag, z) >= policy.EdgeGradientThreshold ? 1 : 0)
                        : (GradientAt(raster, x, z + lag) >= policy.EdgeGradientThreshold ? 1 : 0);
                    count++;
                    sumA += a;
                    sumB += b;
                    sumAB += a * b;
                    sumA2 += a * a;
                    sumB2 += b * b;
                }
            }

            BigInteger covariance = (count * sumAB) - (sumA * sumB);
            BigInteger varianceA = (count * sumA2) - (sumA * sumA);
            BigInteger varianceB = (count * sumB2) - (sumB * sumB);
            BigInteger denominator = BigInteger.Max(varianceA, varianceB);
            int score = covariance <= BigInteger.Zero || denominator <= BigInteger.Zero
                ? 0
                : checked((int)BigInteger.Min(PartsPerMillion, (covariance * PartsPerMillion) / denominator));
            if (score > strongestScore)
            {
                strongestScore = score;
                strongestLag = lag;
            }
        }

        return (strongestScore, strongestLag);
    }

    private static int GradientAt(InspectionRaster raster, int x, int z) =>
        Math.Abs(raster[x, z] - raster[x - 1, z]) +
        Math.Abs(raster[x, z] - raster[x, z - 1]);
}

public sealed record PatternCorpusCase
{
    public PatternCorpusCase(string caseId, bool expectedVisibleVoronoi, PatternInspectionMaps maps)
    {
        if (!ProfileCanonicalEncoding.IsCanonicalIdentifier(caseId))
        {
            throw new ArgumentException("Case ID must be bounded canonical NFC text.", nameof(caseId));
        }

        ArgumentNullException.ThrowIfNull(maps);
        CaseId = caseId;
        ExpectedVisibleVoronoi = expectedVisibleVoronoi;
        Maps = maps;
    }

    public string CaseId { get; }

    public bool ExpectedVisibleVoronoi { get; }

    public PatternInspectionMaps Maps { get; }
}

public enum PatternClassification
{
    TruePositive = 1,
    TrueNegative = 2,
    FalsePositive = 3,
    FalseNegative = 4,
}

public readonly record struct PatternCaseOutcome(
    string CaseId,
    bool ExpectedVisibleVoronoi,
    bool SignalDetected,
    PatternClassification Classification,
    int EdgeAlignmentScorePpm,
    int AxisPeriodicityScorePpm,
    Hash256 MapsChecksum,
    Hash256 DiagnosticChecksum);

public sealed class PatternSensitivityReport
{
    internal PatternSensitivityReport(
        string policyId,
        uint policyVersion,
        Hash256 policyChecksum,
        IEnumerable<PatternCaseOutcome> cases,
        int truePositiveCount,
        int trueNegativeCount,
        int falsePositiveCount,
        int falseNegativeCount,
        int sensitivityPpm,
        int specificityPpm)
    {
        PolicyId = policyId;
        PolicyVersion = policyVersion;
        PolicyChecksum = policyChecksum;
        Cases = Array.AsReadOnly(cases.ToArray());
        TruePositiveCount = truePositiveCount;
        TrueNegativeCount = trueNegativeCount;
        FalsePositiveCount = falsePositiveCount;
        FalseNegativeCount = falseNegativeCount;
        SensitivityPpm = sensitivityPpm;
        SpecificityPpm = specificityPpm;
        RequiresQualitativeReview = true;
        Limitations = Array.AsReadOnly(new[]
        {
            "Thresholds are calibrated to this bounded laboratory corpus and are not universal.",
            "Raster alignment can miss rotated, curved, low-contrast, or sub-pixel polygonal patterns.",
            "Periodicity can flag non-Voronoi structures; qualitative comparison of separate final and edge maps remains required.",
        });
        ContentChecksum = PatternCanonical.ComputeSensitivityHash(this);
    }

    public string PolicyId { get; }

    public uint PolicyVersion { get; }

    public Hash256 PolicyChecksum { get; }

    public ReadOnlyCollection<PatternCaseOutcome> Cases { get; }

    public int TruePositiveCount { get; }

    public int TrueNegativeCount { get; }

    public int FalsePositiveCount { get; }

    public int FalseNegativeCount { get; }

    public int SensitivityPpm { get; }

    public int SpecificityPpm { get; }

    public bool RequiresQualitativeReview { get; }

    public ReadOnlyCollection<string> Limitations { get; }

    public Hash256 ContentChecksum { get; }
}

public static class PatternSensitivityEvaluator
{
    private const int MaximumCorpusCases = 10_000;

    public static GenerationResult<PatternSensitivityReport> Evaluate(
        IReadOnlyList<PatternCorpusCase> corpus,
        PatternDiagnosticPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(policy);
        if (corpus.Count is < 1 or > MaximumCorpusCases)
        {
            return PatternFailure.For<PatternSensitivityReport>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.corpus-capacity",
                Hash256.Zero,
                $"Corpus case count must be in [1, {MaximumCorpusCases.ToString(CultureInfo.InvariantCulture)}].");
        }

        var captured = new PatternCorpusCase[corpus.Count];
        for (int index = 0; index < captured.Length; index++)
        {
            captured[index] = corpus[index] ?? throw new ArgumentException("Corpus cannot contain null cases.", nameof(corpus));
        }

        Array.Sort(captured, static (left, right) => StringComparer.Ordinal.Compare(left.CaseId, right.CaseId));
        for (int index = 1; index < captured.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(captured[index - 1].CaseId, captured[index].CaseId))
            {
                return PatternFailure.For<PatternSensitivityReport>(
                    GenerationFailureCode.InvalidInput,
                    "atlas.pattern.corpus-id",
                    Hash256.Zero,
                    "Corpus case IDs must be unique.");
            }
        }

        BigInteger estimatedCorpusWork = BigInteger.Zero;
        for (int index = 0; index < captured.Length; index++)
        {
            GenerationResult<PatternAnalysisEstimate> estimateResult = PatternDiagnostics.Estimate(captured[index].Maps, policy);
            if (estimateResult is GenerationFailure<PatternAnalysisEstimate> estimateFailure)
            {
                return PatternFailure.For<PatternSensitivityReport>(
                    estimateFailure.Error.Code,
                    estimateFailure.Error.Stage,
                    estimateFailure.Error.InputHash,
                    $"Case {captured[index].CaseId} failed planning: {estimateFailure.Error.Details}");
            }

            estimatedCorpusWork += ((GenerationSuccess<PatternAnalysisEstimate>)estimateResult).Snapshot.WorkUnits;
            if (estimatedCorpusWork > policy.MaximumCorpusAnalysisWorkUnits)
            {
                return PatternFailure.For<PatternSensitivityReport>(
                    GenerationFailureCode.BudgetExceeded,
                    "atlas.pattern.corpus-work-budget",
                    Hash256.Zero,
                    $"Corpus requires {estimatedCorpusWork} planned work units, exceeding explicit policy budget {policy.MaximumCorpusAnalysisWorkUnits}.");
            }
        }

        var outcomes = new PatternCaseOutcome[captured.Length];
        var diagnosticCache = new Dictionary<Hash256, PatternDiagnosticReport>();
        int truePositive = 0;
        int trueNegative = 0;
        int falsePositive = 0;
        int falseNegative = 0;
        for (int index = 0; index < captured.Length; index++)
        {
            PatternCorpusCase item = captured[index];
            if (!diagnosticCache.TryGetValue(item.Maps.ContentChecksum, out PatternDiagnosticReport? diagnostic))
            {
                GenerationResult<PatternDiagnosticReport> diagnosticResult = PatternDiagnostics.Analyze(item.Maps, policy);
                if (diagnosticResult is GenerationFailure<PatternDiagnosticReport> failure)
                {
                    return PatternFailure.For<PatternSensitivityReport>(
                        failure.Error.Code,
                        failure.Error.Stage,
                        failure.Error.InputHash,
                        $"Case {item.CaseId} failed: {failure.Error.Details}");
                }

                diagnostic = ((GenerationSuccess<PatternDiagnosticReport>)diagnosticResult).Snapshot;
                diagnosticCache.Add(item.Maps.ContentChecksum, diagnostic);
            }

            PatternClassification classification;
            if (item.ExpectedVisibleVoronoi && diagnostic.SignalDetected)
            {
                classification = PatternClassification.TruePositive;
                truePositive++;
            }
            else if (!item.ExpectedVisibleVoronoi && !diagnostic.SignalDetected)
            {
                classification = PatternClassification.TrueNegative;
                trueNegative++;
            }
            else if (!item.ExpectedVisibleVoronoi)
            {
                classification = PatternClassification.FalsePositive;
                falsePositive++;
            }
            else
            {
                classification = PatternClassification.FalseNegative;
                falseNegative++;
            }

            outcomes[index] = new PatternCaseOutcome(
                item.CaseId,
                item.ExpectedVisibleVoronoi,
                diagnostic.SignalDetected,
                classification,
                diagnostic.EdgeAlignmentScorePpm,
                diagnostic.AxisPeriodicityScorePpm,
                item.Maps.ContentChecksum,
                diagnostic.ContentChecksum);
        }

        int positives = checked(truePositive + falseNegative);
        int negatives = checked(trueNegative + falsePositive);
        int sensitivity = positives == 0
            ? 0
            : checked((int)(((long)truePositive * PatternDiagnostics.PartsPerMillion) / positives));
        int specificity = negatives == 0
            ? 0
            : checked((int)(((long)trueNegative * PatternDiagnostics.PartsPerMillion) / negatives));
        return GenerationResult<PatternSensitivityReport>.Success(new PatternSensitivityReport(
            policy.PolicyId,
            policy.PolicyVersion,
            policy.ContentChecksum,
            outcomes,
            truePositive,
            trueNegative,
            falsePositive,
            falseNegative,
            sensitivity,
            specificity));
    }
}

public static class InspectionMapRenderer
{
    public static byte[] RenderPortableGraymap(InspectionRaster raster)
    {
        ArgumentNullException.ThrowIfNull(raster);
        byte[] header = Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"P5\n{raster.Width} {raster.Height}\n65535\n"));
        int sampleBytes = checked(raster.Values.Count * 2);
        byte[] output = new byte[checked(header.Length + sampleBytes)];
        header.CopyTo(output, 0);
        int offset = header.Length;
        foreach (ushort value in raster.Values)
        {
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(offset, 2), value);
            offset += 2;
        }

        return output;
    }

    public static byte[] RenderBitmap24(InspectionRaster raster)
    {
        ArgumentNullException.ThrowIfNull(raster);
        int rowBytes = checked(raster.Width * 3);
        int paddedRowBytes = checked((rowBytes + 3) & ~3);
        int pixelBytes = checked(paddedRowBytes * raster.Height);
        const int pixelOffset = 54;
        byte[] output = new byte[checked(pixelOffset + pixelBytes)];
        output[0] = (byte)'B';
        output[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(2, 4), output.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(10, 4), pixelOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(18, 4), raster.Width);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(22, 4), raster.Height);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(28, 2), 24);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(34, 4), pixelBytes);

        for (int outputRow = 0; outputRow < raster.Height; outputRow++)
        {
            int sourceZ = raster.Height - outputRow - 1;
            int rowOffset = pixelOffset + (outputRow * paddedRowBytes);
            for (int x = 0; x < raster.Width; x++)
            {
                byte gray = checked((byte)(raster[x, sourceZ] >> 8));
                int pixel = rowOffset + (x * 3);
                output[pixel] = gray;
                output[pixel + 1] = gray;
                output[pixel + 2] = gray;
            }
        }

        return output;
    }
}

internal static class PatternCanonical
{
    internal static Hash256 ComputePolicyHash(PatternDiagnosticPolicy policy)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, policy.PolicyId);
        AppendUInt32(hash, policy.PolicyVersion);
        AppendInt32(hash, policy.EdgeGradientThreshold);
        AppendInt32(hash, policy.EdgeAlignmentThresholdPpm);
        AppendInt32(hash, policy.PeriodicityThresholdPpm);
        AppendInt32(hash, policy.MinimumPeriodLag);
        AppendInt32(hash, policy.MaximumPeriodLag);
        AppendInt64(hash, policy.MaximumAnalysisWorkUnits);
        AppendInt64(hash, policy.MaximumCorpusAnalysisWorkUnits);
        return Hash256.FromCanonicalBytes(hash.GetHashAndReset());
    }

    internal static Hash256 ComputeRasterHash(int width, int height, IReadOnlyList<ushort> values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, width);
        AppendInt32(hash, height);
        Span<byte> encoded = stackalloc byte[2];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(encoded, values[index]);
            hash.AppendData(encoded);
        }

        return Hash256.FromCanonicalBytes(hash.GetHashAndReset());
    }

    internal static Hash256 ComputeMapPairHash(PatternInspectionMaps maps) =>
        ComputeMapPairHash(maps.FinalOutput, maps.AtlasEdges);

    internal static Hash256 ComputeMapPairHash(InspectionRaster finalOutput, InspectionRaster atlasEdges)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "final-output");
        AppendHash(hash, finalOutput.ContentChecksum);
        AppendText(hash, "atlas-edges");
        AppendHash(hash, atlasEdges.ContentChecksum);
        return Hash256.FromCanonicalBytes(hash.GetHashAndReset());
    }

    internal static Hash256 ComputeDiagnosticHash(PatternDiagnosticReport report)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, report.PolicyId);
        AppendUInt32(hash, report.PolicyVersion);
        AppendHash(hash, report.PolicyChecksum);
        AppendHash(hash, report.MapsChecksum);
        AppendInt32(hash, report.EdgeAlignmentScorePpm);
        AppendInt32(hash, report.AxisPeriodicityScorePpm);
        AppendInt32(hash, report.StrongestHorizontalLag);
        AppendInt32(hash, report.StrongestVerticalLag);
        AppendInt32(hash, report.EdgeAlignmentExceeded ? 1 : 0);
        AppendInt32(hash, report.PeriodicityExceeded ? 1 : 0);
        foreach (string finding in report.Findings)
        {
            AppendText(hash, finding);
        }

        return Hash256.FromCanonicalBytes(hash.GetHashAndReset());
    }

    internal static Hash256 ComputeSensitivityHash(PatternSensitivityReport report)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, report.PolicyId);
        AppendUInt32(hash, report.PolicyVersion);
        AppendHash(hash, report.PolicyChecksum);
        AppendInt32(hash, report.TruePositiveCount);
        AppendInt32(hash, report.TrueNegativeCount);
        AppendInt32(hash, report.FalsePositiveCount);
        AppendInt32(hash, report.FalseNegativeCount);
        AppendInt32(hash, report.SensitivityPpm);
        AppendInt32(hash, report.SpecificityPpm);
        foreach (PatternCaseOutcome item in report.Cases)
        {
            AppendText(hash, item.CaseId);
            AppendInt32(hash, item.ExpectedVisibleVoronoi ? 1 : 0);
            AppendInt32(hash, item.SignalDetected ? 1 : 0);
            AppendInt32(hash, (int)item.Classification);
            AppendInt32(hash, item.EdgeAlignmentScorePpm);
            AppendInt32(hash, item.AxisPeriodicityScorePpm);
            AppendHash(hash, item.MapsChecksum);
            AppendHash(hash, item.DiagnosticChecksum);
        }

        foreach (string limitation in report.Limitations)
        {
            AppendText(hash, limitation);
        }

        return Hash256.FromCanonicalBytes(hash.GetHashAndReset());
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, encoded.Length);
        hash.AppendData(encoded);
    }

    private static void AppendHash(IncrementalHash hash, Hash256 value)
    {
        Span<byte> encoded = stackalloc byte[Hash256.ByteWidth];
        value.WriteCanonicalBytes(encoded);
        hash.AppendData(encoded);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> encoded = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(encoded, value);
        hash.AppendData(encoded);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> encoded = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(encoded, value);
        hash.AppendData(encoded);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> encoded = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(encoded, value);
        hash.AppendData(encoded);
    }
}

internal static class PatternFailure
{
    internal static GenerationResult<T> For<T>(
        GenerationFailureCode code,
        string stage,
        Hash256 inputHash,
        string details)
        where T : class => GenerationResult<T>.Failure(new GenerationError(
            code,
            nativeSeed: 0,
            stage,
            StableId.Zero,
            inputHash,
            details,
            canRetry: false));
}
