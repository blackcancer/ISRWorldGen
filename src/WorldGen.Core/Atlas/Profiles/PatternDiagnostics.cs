using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.Profiles;

public sealed class InspectionRaster
{
    private InspectionRaster(int width, int height, ushort[] values)
    {
        Width = width;
        Height = height;
        Values = Array.AsReadOnly(values);
        ContentChecksum = PatternCanonical.ComputeRasterHash(width, height, values);
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyCollection<ushort> Values { get; }

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
        IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        BigInteger cellCount = (BigInteger)width * height;
        if (width <= 0 || height <= 0 || cellCount > Array.MaxLength)
        {
            return PatternFailure.For<InspectionRaster>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.raster-capacity",
                Hash256.Zero,
                "Raster dimensions are non-positive or exceed the runtime array capacity.");
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

        return GenerationResult<InspectionRaster>.Success(new InspectionRaster(width, height, copy));
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
        InspectionRaster atlasEdges)
    {
        ArgumentNullException.ThrowIfNull(finalOutput);
        ArgumentNullException.ThrowIfNull(atlasEdges);
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
        int maximumPeriodLag)
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

        PolicyId = policyId;
        PolicyVersion = policyVersion;
        EdgeGradientThreshold = edgeGradientThreshold;
        EdgeAlignmentThresholdPpm = edgeAlignmentThresholdPpm;
        PeriodicityThresholdPpm = periodicityThresholdPpm;
        MinimumPeriodLag = minimumPeriodLag;
        MaximumPeriodLag = maximumPeriodLag;
    }

    public string PolicyId { get; }

    public uint PolicyVersion { get; }

    public int EdgeGradientThreshold { get; }

    public int EdgeAlignmentThresholdPpm { get; }

    public int PeriodicityThresholdPpm { get; }

    public int MinimumPeriodLag { get; }

    public int MaximumPeriodLag { get; }
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

public static class PatternDiagnostics
{
    public const int PartsPerMillion = 1_000_000;

    public static GenerationResult<PatternDiagnosticReport> Analyze(
        PatternInspectionMaps maps,
        PatternDiagnosticPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.MaximumPeriodLag >= maps.FinalOutput.Width ||
            policy.MaximumPeriodLag >= maps.FinalOutput.Height)
        {
            return PatternFailure.For<PatternDiagnosticReport>(
                GenerationFailureCode.InvalidInput,
                "atlas.pattern.policy-range",
                maps.ContentChecksum,
                "Diagnostic lag range does not fit both raster axes.");
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

        var outcomes = new PatternCaseOutcome[captured.Length];
        int truePositive = 0;
        int trueNegative = 0;
        int falsePositive = 0;
        int falseNegative = 0;
        for (int index = 0; index < captured.Length; index++)
        {
            PatternCorpusCase item = captured[index];
            GenerationResult<PatternDiagnosticReport> diagnosticResult = PatternDiagnostics.Analyze(item.Maps, policy);
            if (diagnosticResult is GenerationFailure<PatternDiagnosticReport> failure)
            {
                return PatternFailure.For<PatternSensitivityReport>(
                    failure.Error.Code,
                    failure.Error.Stage,
                    failure.Error.InputHash,
                    $"Case {item.CaseId} failed: {failure.Error.Details}");
            }

            PatternDiagnosticReport diagnostic = ((GenerationSuccess<PatternDiagnosticReport>)diagnosticResult).Snapshot;
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
        int sensitivity = positives == 0 ? 0 : checked((truePositive * PatternDiagnostics.PartsPerMillion) / positives);
        int specificity = negatives == 0 ? 0 : checked((trueNegative * PatternDiagnostics.PartsPerMillion) / negatives);
        return GenerationResult<PatternSensitivityReport>.Success(new PatternSensitivityReport(
            policy.PolicyId,
            policy.PolicyVersion,
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
