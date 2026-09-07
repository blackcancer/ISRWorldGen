using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;

namespace ISRWorldGen.Tests.L03B;

/// <summary>
/// Deterministic, in-memory prequalification only. It deliberately neither reads
/// nor writes Evidence artefacts: a future evidence campaign owns that boundary.
/// Every card and profile contains the published absolute model altitude without
/// display stretching, and every radius is an unmodified physical block distance.
/// </summary>
internal static class L03BMultiscalePrequalification
{
    internal const int MapSide = 17;
    internal const int ProfilePointCount = 17;
    private const int MinimumBlindSecretBytes = 16;

    internal static IReadOnlyList<L03BMultiscaleFixture> CreateFamilyFixtures(
        LandscapeModel model,
        AtlasMesh atlas,
        int seed)
    {
        var sites = atlas.Sites.ToDictionary(site => site.Id);
        return Enum.GetValues<LandscapeFamily>().Select(family =>
        {
            LandscapeCellProfile cell = model.Cells
                .Where(candidate => candidate.Family == family)
                .FirstOrDefault(candidate =>
                {
                    AtlasSite site = sites[candidate.CellId];
                    return EdgeMargin(atlas.Bounds, site.X, site.Z) >= candidate.PhysicalScaleRadii.RegionRadiusBlocks;
                });
            if (cell.CellId == StableId.Zero)
            {
                throw new InvalidOperationException(
                    $"No unclipped {family} fixture contains its declared regional radius.");
            }

            AtlasSite selected = sites[cell.CellId];
            return new L03BMultiscaleFixture(
                family,
                seed,
                selected.X,
                selected.Z,
                atlas.Bounds,
                cell.PhysicalScaleRadii)
            {
                CellId = cell.CellId,
            };
        }).ToArray();
    }

    internal static L03BFixtureAssessment Assess(
        LandscapeModel model,
        AtlasMesh atlas,
        L03BMultiscaleFixture fixture)
    {
        if (EdgeMargin(fixture.Bounds, fixture.CenterX, fixture.CenterZ) < fixture.Radii.RegionRadiusBlocks)
        {
            throw new InvalidOperationException("Physical review radii must fit without edge clipping or clamping.");
        }

        IReadOnlyList<L03BTransitionAxis> transitionAxes = FindTransitionAxes(model, atlas, fixture);
        L03BScaleMeasurement[] scales =
        [
            Measure(model, fixture, L03BScale.Core, fixture.Radii.CoreRadiusBlocks, transitionAxes[0]),
            Measure(model, fixture, L03BScale.Detail, fixture.Radii.DetailRadiusBlocks, transitionAxes[1]),
            Measure(model, fixture, L03BScale.Region, fixture.Radii.RegionRadiusBlocks, transitionAxes[2]),
        ];
        return new L03BFixtureAssessment(fixture, scales);
    }

    internal static L03BBlindReviewPacket CreateBlindReviewPacket(
        IEnumerable<L03BFixtureAssessment> assessments,
        ReadOnlySpan<byte> permutationSecret)
    {
        L03BBlindedAssessment[] blinded = Blind(assessments, permutationSecret);
        var cards = new List<L03BBlindMapCard>();
        var profiles = new List<L03BBlindAbsoluteProfile>();
        var metrics = new List<L03BBlindPilotLine>();
        foreach (L03BBlindedAssessment item in blinded)
        {
            foreach (L03BScaleMeasurement scale in item.Assessment.Scales.OrderBy(value => value.Scale))
            {
                cards.Add(new L03BBlindMapCard(
                    item.Code,
                    scale.Scale,
                    scale.RadiusBlocks,
                    scale.MapSide,
                    Array.AsReadOnly(scale.MapSamples.Select(point => point.Sample.ModelAltitudeNormalized).ToArray())));
                profiles.Add(new L03BBlindAbsoluteProfile(
                    item.Code,
                    scale.Scale,
                    scale.RadiusBlocks,
                    Array.AsReadOnly(scale.TransitionProfile.Points.Select(point => point.SignedOffsetBlocks).ToArray()),
                    Array.AsReadOnly(scale.TransitionProfile.Points.Select(point => point.Sample.ModelAltitudeNormalized).ToArray()),
                    Array.AsReadOnly(scale.TransitionProfile.Points.Select(point => point.Sample.IsTransition).ToArray()),
                    Array.AsReadOnly(scale.TransitionProfile.Points.Select(point => point.Sample.ForeignResidualContributionNormalized).ToArray())));
                metrics.Add(new L03BBlindPilotLine(
                    item.Code,
                    scale.Scale,
                    scale.RadiusBlocks,
                    scale.MinimumAbsoluteAltitude,
                    scale.MaximumAbsoluteAltitude,
                    scale.AbsoluteAmplitude,
                    scale.MaximumAbsoluteProminence,
                    scale.UniqueMapPointCount,
                    scale.PureCoreSampleCount,
                    scale.ConnectedPureCoreSamples,
                    scale.PureCoreBoundarySampleCount,
                    scale.TransitionSampleCount,
                    scale.NonZeroForeignTransitionSampleCount,
                    scale.MinimumAbsoluteForeignResidualContribution,
                    scale.MaximumAbsoluteForeignResidualContribution));
            }
        }

        return new L03BBlindReviewPacket(cards.AsReadOnly(), profiles.AsReadOnly(), metrics.AsReadOnly());
    }

    internal static IReadOnlyList<L03BBlindAnswerKeyLine> CreateBlindAnswerKey(
        IEnumerable<L03BFixtureAssessment> assessments,
        ReadOnlySpan<byte> permutationSecret) => Blind(assessments, permutationSecret)
            .Select(item => new L03BBlindAnswerKeyLine(
                item.Code,
                item.Assessment.Fixture.Family,
                item.Assessment.Fixture.Seed,
                item.Assessment.Fixture.CellId,
                item.Assessment.Fixture.CenterX,
                item.Assessment.Fixture.CenterZ))
            .ToArray();

    internal static string RenderBlindReview(L03BBlindReviewPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new StringBuilder();
        output.AppendLine("[absolute-map-cards]");
        output.AppendLine("fixture,scale,radiusBlocks,side,row,column,absoluteAltitude");
        foreach (L03BBlindMapCard card in packet.Cards)
        {
            for (int index = 0; index < card.AbsoluteAltitudes.Count; index++)
            {
                output.Append(card.Code).Append(',').Append(card.Scale).Append(',')
                    .Append(card.RadiusBlocks.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(card.Side.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append((index / card.Side).ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append((index % card.Side).ToString(CultureInfo.InvariantCulture)).Append(',')
                    .AppendLine(Number(card.AbsoluteAltitudes[index]));
            }
        }

        output.AppendLine("[absolute-transition-profiles]");
        output.AppendLine("fixture,scale,radiusBlocks,offsetBlocks,absoluteAltitude,isTransition,foreignResidualContribution");
        foreach (L03BBlindAbsoluteProfile profile in packet.Profiles)
        {
            for (int index = 0; index < profile.AbsoluteAltitudes.Count; index++)
            {
                output.Append(profile.Code).Append(',').Append(profile.Scale).Append(',')
                    .Append(profile.RadiusBlocks.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(profile.SignedOffsetsBlocks[index].ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(Number(profile.AbsoluteAltitudes[index])).Append(',')
                    .Append(profile.IsTransition[index] ? "true" : "false").Append(',')
                    .AppendLine(Number(profile.ForeignResidualContributions[index]));
            }
        }

        output.AppendLine("[measurements]");
        output.AppendLine("fixture,scale,radiusBlocks,minAbsolute,maxAbsolute,amplitude,prominence,uniqueMapPoints,pureCoreSamples,connectedPureCoreSamples,pureCoreBoundarySamples,transitionSamples,nonZeroForeignTransitionSamples,minAbsoluteForeignContribution,maxAbsoluteForeignContribution");
        foreach (L03BBlindPilotLine line in packet.Metrics)
        {
            output.Append(line.Code).Append(',').Append(line.Scale).Append(',')
                .Append(line.RadiusBlocks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Number(line.MinimumAbsoluteAltitude)).Append(',')
                .Append(Number(line.MaximumAbsoluteAltitude)).Append(',')
                .Append(Number(line.AbsoluteAmplitude)).Append(',')
                .Append(Number(line.MaximumAbsoluteProminence)).Append(',')
                .Append(line.UniqueMapPointCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(line.PureCoreSampleCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(line.ConnectedPureCoreSamples.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(line.PureCoreBoundarySampleCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(line.TransitionSampleCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(line.NonZeroForeignTransitionSampleCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Number(line.MinimumAbsoluteForeignResidualContribution)).Append(',')
                .AppendLine(Number(line.MaximumAbsoluteForeignResidualContribution));
        }

        return output.ToString();
    }

    internal static string RenderBlindAnswerKey(IEnumerable<L03BBlindAnswerKeyLine> key) => string.Join('\n',
        new[] { "fixture,family,seed,cellId,centerX,centerZ" }.Concat(key.Select(line => string.Join(',',
            line.Code,
            line.Family.ToString(),
            line.Seed.ToString(CultureInfo.InvariantCulture),
            line.CellId.ToString(),
            line.CenterX.ToString(CultureInfo.InvariantCulture),
            line.CenterZ.ToString(CultureInfo.InvariantCulture)))));

    internal static int CountConnectedFromCenter(IReadOnlyList<bool> mask, int side)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (side <= 0 || mask.Count != checked(side * side))
        {
            throw new ArgumentOutOfRangeException(nameof(side), "Connectivity mask must be one non-empty square.");
        }

        int origin = (side / 2 * side) + (side / 2);
        if (!mask[origin])
        {
            return 0;
        }

        var visited = new bool[mask.Count];
        var queue = new Queue<int>();
        visited[origin] = true;
        queue.Enqueue(origin);
        int connected = 0;
        while (queue.TryDequeue(out int index))
        {
            connected++;
            int row = index / side;
            int column = index % side;
            Visit(row - 1, column);
            Visit(row + 1, column);
            Visit(row, column - 1);
            Visit(row, column + 1);
        }

        return connected;

        void Visit(int row, int column)
        {
            if (row < 0 || row >= side || column < 0 || column >= side)
            {
                return;
            }

            int candidate = (row * side) + column;
            if (mask[candidate] && !visited[candidate])
            {
                visited[candidate] = true;
                queue.Enqueue(candidate);
            }
        }
    }

    private static L03BScaleMeasurement Measure(
        LandscapeModel model,
        L03BMultiscaleFixture fixture,
        L03BScale scale,
        long radius,
        L03BTransitionAxis transitionAxis)
    {
        (long X, long Z)[] locations = Grid(fixture.CenterX, fixture.CenterZ, radius, MapSide);
        L03BMapPoint[] samples = locations
            .Select(point => new L03BMapPoint(point.X, point.Z, model.Sample(point.X, point.Z)))
            .ToArray();
        bool[] pureCoreMask = samples.Select(point =>
            point.Sample.DominantCellId == fixture.CellId &&
            !point.Sample.IsTransition &&
            point.Sample.ForeignResidualWeight == 0d &&
            point.Sample.ForeignResidualContributionNormalized == 0d).ToArray();
        int connectedPureCore = CountConnectedFromCenter(pureCoreMask, MapSide);
        int pureBoundary = Enumerable.Range(0, samples.Length).Count(index =>
        {
            int row = index / MapSide;
            int column = index % MapSide;
            return (row == 0 || row == MapSide - 1 || column == 0 || column == MapSide - 1) && pureCoreMask[index];
        });

        L03BTransitionProfile transitionProfile = SampleTransitionProfile(model, fixture.Bounds, transitionAxis, radius);
        L03BTransitionPoint[] nonZeroForeign = transitionProfile.Points.Where(point =>
            point.Sample.IsTransition &&
            point.Sample.ForeignResidualWeight > 0d &&
            point.Sample.ForeignResidualContributionNormalized != 0d).ToArray();
        double[] absolute = samples.Select(point => point.Sample.ModelAltitudeNormalized).ToArray();
        double mean = absolute.Average();
        return new L03BScaleMeasurement(
            scale,
            radius,
            MapSide,
            absolute.Min(),
            absolute.Max(),
            absolute.Max() - absolute.Min(),
            absolute.Max(value => Math.Abs(value - mean)),
            samples.Length,
            samples.Select(point => (point.X, point.Z)).Distinct().Count(),
            pureCoreMask.Count(value => value),
            connectedPureCore,
            pureBoundary,
            transitionProfile.Points.Count(point => point.Sample.IsTransition),
            nonZeroForeign.Length,
            nonZeroForeign.Length == 0 ? 0d : nonZeroForeign.Min(point => Math.Abs(point.Sample.ForeignResidualContributionNormalized)),
            nonZeroForeign.Length == 0 ? 0d : nonZeroForeign.Max(point => Math.Abs(point.Sample.ForeignResidualContributionNormalized)),
            samples,
            transitionProfile);
    }

    private static IReadOnlyList<L03BTransitionAxis> FindTransitionAxes(
        LandscapeModel model,
        AtlasMesh atlas,
        L03BMultiscaleFixture fixture)
    {
        VoronoiCell cell = atlas.Cells.Single(candidate => candidate.SiteId == fixture.CellId);
        var sites = atlas.Sites.ToDictionary(site => site.Id);
        AtlasSite owner = sites[fixture.CellId];
        long requiredMargin = fixture.Radii.RegionRadiusBlocks;
        var axes = new List<L03BTransitionAxis>(3);
        var centers = new HashSet<(long X, long Z)>();
        foreach (StableId neighborId in cell.NeighborIds)
        {
            AtlasSite neighbor = sites[neighborId];
            double dx = neighbor.X - owner.X;
            double dz = neighbor.Z - owner.Z;
            double length = Math.Sqrt((dx * dx) + (dz * dz));
            if (!double.IsFinite(length) || length <= 0)
            {
                continue;
            }

            for (int perMille = 350; perMille <= 650; perMille++)
            {
                long x = checked((long)Math.Round(owner.X + (dx * perMille / 1000d), MidpointRounding.AwayFromZero));
                long z = checked((long)Math.Round(owner.Z + (dz * perMille / 1000d), MidpointRounding.AwayFromZero));
                if (!centers.Add((x, z)) || EdgeMargin(atlas.Bounds, x, z) < requiredMargin)
                {
                    continue;
                }

                LandscapeSample sample = model.Sample(x, z);
                if (!sample.IsTransition || sample.ForeignResidualWeight <= 0d ||
                    sample.ForeignResidualContributionNormalized == 0d)
                {
                    continue;
                }

                axes.Add(new L03BTransitionAxis(x, z, dx / length, dz / length));
                if (axes.Count == 3)
                {
                    return axes;
                }
            }
        }

        throw new InvalidOperationException(
            $"Fixture {fixture.Family} has fewer than three distinct, unclipped transition probes with non-zero foreign residuals.");
    }

    private static L03BTransitionProfile SampleTransitionProfile(
        LandscapeModel model,
        WorldBounds bounds,
        L03BTransitionAxis axis,
        long radius)
    {
        long[] offsets = LinearOffsets(radius, ProfilePointCount);
        L03BTransitionPoint[] points = offsets.Select(offset =>
        {
            long x = checked((long)Math.Round(axis.CenterX + (axis.UnitX * offset), MidpointRounding.AwayFromZero));
            long z = checked((long)Math.Round(axis.CenterZ + (axis.UnitZ * offset), MidpointRounding.AwayFromZero));
            if (!bounds.Contains(x, z))
            {
                throw new InvalidOperationException("An absolute transition profile exceeded the world instead of being rejected as unclippable.");
            }

            return new L03BTransitionPoint(x, z, offset, model.Sample(x, z));
        }).ToArray();
        if (points.Select(point => (point.X, point.Z)).Distinct().Count() != points.Length)
        {
            throw new InvalidOperationException("A physical transition profile collapsed distinct offsets onto duplicate points.");
        }

        return new L03BTransitionProfile(axis.CenterX, axis.CenterZ, points);
    }

    private static (long X, long Z)[] Grid(long centerX, long centerZ, long radius, int side)
    {
        long[] offsets = LinearOffsets(radius, side);
        (long X, long Z)[] points = offsets.SelectMany(z => offsets.Select(x =>
            (checked(centerX + x), checked(centerZ + z)))).ToArray();
        if (points.Distinct().Count() != points.Length)
        {
            throw new InvalidOperationException("A physical map grid collapsed onto duplicate points.");
        }

        return points;
    }

    private static long[] LinearOffsets(long radius, int count)
    {
        if (radius <= 0 || count < 3 || count % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Physical sampling requires a positive radius and an odd point count.");
        }

        long[] offsets = Enumerable.Range(0, count)
            .Select(index => ((2L * radius * index) / (count - 1)) - radius)
            .ToArray();
        if (offsets.Distinct().Count() != offsets.Length)
        {
            throw new InvalidOperationException("Physical radius is too small for unique sampling points.");
        }

        return offsets;
    }

    private static long EdgeMargin(WorldBounds bounds, long x, long z) => Math.Min(
        Math.Min(x - bounds.MinX, bounds.MaxXExclusive - 1 - x),
        Math.Min(z - bounds.MinZ, bounds.MaxZExclusive - 1 - z));

    private static L03BBlindedAssessment[] Blind(
        IEnumerable<L03BFixtureAssessment> assessments,
        ReadOnlySpan<byte> permutationSecret)
    {
        byte[] secret = permutationSecret.ToArray();
        if (secret.Length < MinimumBlindSecretBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(permutationSecret), "Blind permutation secret must contain at least 128 bits.");
        }

        L03BFixtureAssessment[] source = assessments.ToArray();
        if (source.Any(item => item is null) || source.Select(item => item.Fixture.CellId).Distinct().Count() != source.Length)
        {
            throw new ArgumentException("Blind fixtures must be non-null and uniquely identified.", nameof(assessments));
        }

        L03BBlindedAssessment[] blinded = source.Select(assessment =>
        {
            byte[] codeDigest = BlindDigest(secret, "l03b-blind-code-v1", assessment.Fixture);
            byte[] orderDigest = BlindDigest(secret, "l03b-blind-order-v1", assessment.Fixture);
            return new L03BBlindedAssessment(
                $"Q-{Convert.ToHexString(codeDigest.AsSpan(0, 8))}",
                Convert.ToHexString(orderDigest),
                assessment);
        }).OrderBy(item => item.OrderKey, StringComparer.Ordinal).ToArray();
        if (blinded.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != blinded.Length)
        {
            throw new InvalidOperationException("Opaque blind fixture identifiers collided.");
        }

        return blinded;
    }

    private static byte[] BlindDigest(byte[] secret, string domain, L03BMultiscaleFixture fixture)
    {
        byte[] domainBytes = Encoding.UTF8.GetBytes(domain);
        byte[] payload = new byte[checked(domainBytes.Length + 20)];
        domainBytes.CopyTo(payload, 0);
        fixture.CellId.WriteCanonicalBytes(payload.AsSpan(domainBytes.Length, 16));
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(domainBytes.Length + 16, 4), fixture.Seed);
        return HMACSHA256.HashData(secret, payload);
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

internal enum L03BScale { Core, Detail, Region }

internal sealed record L03BMultiscaleFixture(
    LandscapeFamily Family,
    int Seed,
    long CenterX,
    long CenterZ,
    WorldBounds Bounds,
    LandscapePhysicalScaleRadii Radii)
{
    internal StableId CellId { get; init; }
}

internal sealed record L03BFixtureAssessment(
    L03BMultiscaleFixture Fixture,
    IReadOnlyList<L03BScaleMeasurement> Scales);

internal sealed record L03BScaleMeasurement(
    L03BScale Scale,
    long RadiusBlocks,
    int MapSide,
    double MinimumAbsoluteAltitude,
    double MaximumAbsoluteAltitude,
    double AbsoluteAmplitude,
    double MaximumAbsoluteProminence,
    int MapPointCount,
    int UniqueMapPointCount,
    int PureCoreSampleCount,
    int ConnectedPureCoreSamples,
    int PureCoreBoundarySampleCount,
    int TransitionSampleCount,
    int NonZeroForeignTransitionSampleCount,
    double MinimumAbsoluteForeignResidualContribution,
    double MaximumAbsoluteForeignResidualContribution,
    IReadOnlyList<L03BMapPoint> MapSamples,
    L03BTransitionProfile TransitionProfile);

internal sealed record L03BMapPoint(long X, long Z, LandscapeSample Sample);

internal sealed record L03BTransitionPoint(long X, long Z, long SignedOffsetBlocks, LandscapeSample Sample);

internal sealed record L03BTransitionProfile(
    long CenterX,
    long CenterZ,
    IReadOnlyList<L03BTransitionPoint> Points);

internal readonly record struct L03BTransitionAxis(
    long CenterX,
    long CenterZ,
    double UnitX,
    double UnitZ);

/// <summary>Neutral reviewer packet. It has no family, seed, cell ID or absolute coordinate.</summary>
internal sealed record L03BBlindReviewPacket(
    IReadOnlyList<L03BBlindMapCard> Cards,
    IReadOnlyList<L03BBlindAbsoluteProfile> Profiles,
    IReadOnlyList<L03BBlindPilotLine> Metrics);

internal sealed record L03BBlindMapCard(
    string Code,
    L03BScale Scale,
    long RadiusBlocks,
    int Side,
    IReadOnlyList<double> AbsoluteAltitudes);

internal sealed record L03BBlindAbsoluteProfile(
    string Code,
    L03BScale Scale,
    long RadiusBlocks,
    IReadOnlyList<long> SignedOffsetsBlocks,
    IReadOnlyList<double> AbsoluteAltitudes,
    IReadOnlyList<bool> IsTransition,
    IReadOnlyList<double> ForeignResidualContributions);

internal sealed record L03BBlindPilotLine(
    string Code,
    L03BScale Scale,
    long RadiusBlocks,
    double MinimumAbsoluteAltitude,
    double MaximumAbsoluteAltitude,
    double AbsoluteAmplitude,
    double MaximumAbsoluteProminence,
    int UniqueMapPointCount,
    int PureCoreSampleCount,
    int ConnectedPureCoreSamples,
    int PureCoreBoundarySampleCount,
    int TransitionSampleCount,
    int NonZeroForeignTransitionSampleCount,
    double MinimumAbsoluteForeignResidualContribution,
    double MaximumAbsoluteForeignResidualContribution);

/// <summary>The family mapping exists only in this separately rendered controller key.</summary>
internal sealed record L03BBlindAnswerKeyLine(
    string Code,
    LandscapeFamily Family,
    int Seed,
    StableId CellId,
    long CenterX,
    long CenterZ);

internal sealed record L03BBlindedAssessment(
    string Code,
    string OrderKey,
    L03BFixtureAssessment Assessment);
