using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;

namespace ISRWorldGen.Tests.L03B;

/// <summary>
/// Evidence-only view selection.  A view is accepted only when every published
/// pixel belongs to one owner core; transition samples are never rendered or
/// included in morphology measurements.
/// </summary>
internal static class L03BEvidenceViews
{
    internal const int BlindMapSide = 193;
    internal const int CorpusMapSide = 65;
    internal const double RequestedMacroSpanFactor = 1.7d;
    internal const double MinimumMacroSpanFactor = .35d;

    private static readonly double[] SpanFactors = [1.7d, 1.5d, 1.3d, 1.1d, .9d, .75d, .6d, .5d, .425d, .35d];

    internal static L03BPureLandscapeView SelectPureView(
        LandscapeModel model,
        AtlasMesh atlas,
        LandscapeFamily family,
        int seed,
        int side)
    {
        L03BPureLandscapeView? view = TrySelectPureView(model, atlas, family, seed, side);
        return view ?? throw new InvalidOperationException(
            $"No {family} owner core contains the declared minimum {MinimumMacroSpanFactor:R}x macro span.");
    }

    internal static L03BPureLandscapeView? TrySelectPureView(
        LandscapeModel model,
        AtlasMesh atlas,
        LandscapeFamily family,
        int seed,
        int side)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(atlas);
        if (side < 17 || side % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(side), "Evidence views require an odd side of at least 17 pixels.");
        }

        LandscapeFamilyProfile profile = LandscapeFamilyCatalog.Get(family);
        var sites = atlas.Sites.ToDictionary(site => site.Id);
        LandscapeCellProfile[] candidates = model.Cells
            .Where(cell => cell.Family == family)
            .OrderBy(cell => cell.CellId, StableIdComparer.Instance)
            .ToArray();

        foreach (double factor in SpanFactors)
        {
            double spanBlocks = profile.MacroWavelengthBlocks * factor;
            foreach (LandscapeCellProfile cell in candidates)
            {
                AtlasSite site = sites[cell.CellId];
                double halfSpan = spanBlocks / 2d;
                if (site.X - halfSpan < atlas.Bounds.MinX || site.X + halfSpan > atlas.Bounds.MaxXExclusive - 1 ||
                    site.Z - halfSpan < atlas.Bounds.MinZ || site.Z + halfSpan > atlas.Bounds.MaxZExclusive - 1)
                {
                    continue;
                }

                L03BPureLandscapeView? view = TrySample(model, cell, site, seed, side, spanBlocks);
                if (view is not null)
                {
                    return view;
                }
            }
        }

        return null;
    }

    internal static void RequirePure(L03BPureLandscapeView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Side < 17 || view.Side % 2 == 0 || view.Pixels.Count != checked(view.Side * view.Side))
        {
            throw new InvalidDataException("Evidence view dimensions and pixel count disagree.");
        }
        if (!double.IsFinite(view.SpanBlocks) || view.SpanBlocks <= 0 ||
            !double.IsFinite(view.StepBlocks) || view.StepBlocks <= 0)
        {
            throw new InvalidDataException("Evidence view physical scale must be finite and positive.");
        }
        if (view.Pixels.Select(pixel => (pixel.X, pixel.Z)).Distinct().Count() != view.Pixels.Count)
        {
            throw new InvalidDataException("Evidence pixels must use unique physical coordinates.");
        }
        if (view.Pixels.Any(pixel => pixel.Row < 0 || pixel.Row >= view.Side || pixel.Column < 0 || pixel.Column >= view.Side) ||
            view.Pixels.Select(pixel => (pixel.Row, pixel.Column)).Distinct().Count() != view.Pixels.Count)
        {
            throw new InvalidDataException("Evidence pixels must cover every declared row and column exactly once.");
        }

        foreach (L03BEvidencePixel pixel in view.Pixels)
        {
            if (pixel.Sample.DominantCellId != view.OwnerCellId ||
                pixel.Sample.DominantFamily != view.Family ||
                pixel.Sample.IsTransition ||
                pixel.Sample.ActiveResidualContributorCount != 1 ||
                pixel.Sample.PrimaryResidualWeight != 1d ||
                pixel.Sample.ForeignResidualWeight != 0d ||
                pixel.Sample.ForeignResidualContributionNormalized != 0d)
            {
                throw new InvalidDataException(
                    "Evidence views reject every ownership transition and foreign residual contribution.");
            }
        }
    }

    internal static double[,] Altitudes(L03BPureLandscapeView view)
    {
        RequirePure(view);
        var result = new double[view.Side, view.Side];
        foreach (L03BEvidencePixel pixel in view.Pixels)
        {
            result[pixel.Row, pixel.Column] = pixel.Sample.ModelAltitudeNormalized;
        }
        return result;
    }

    internal static byte[] RenderTransitionMask(L03BPureLandscapeView view)
    {
        RequirePure(view);
        string header = $"P5\n{view.Side} {view.Side}\n255\n";
        byte[] headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
        byte[] result = new byte[checked(headerBytes.Length + view.Pixels.Count)];
        headerBytes.CopyTo(result, 0);
        // Zero is owner-pure. RequirePure makes a non-zero pixel impossible.
        return result;
    }

    internal static L03BMorphologyMeasurement MeasureMorphology(L03BPureLandscapeView view)
    {
        if (view.Side < 53)
        {
            throw new ArgumentOutOfRangeException(nameof(view), "Morphology measurements require at least 53 pixels per side for the declared ring probes.");
        }
        double[,] values = Altitudes(view);
        double[,] residual = Detrend(values);
        double[] gradients = GradientMagnitudes(residual);
        double[] curvatures = Curvatures(residual);
        double span = residual.Cast<double>().Max() - residual.Cast<double>().Min();
        double nominalIncrement = span / view.Side;
        double[,] smooth = BoxBlur(BoxBlur(residual));
        (double craterLift, double coneDrop) = BestNestedRelief(smooth, 5, 15);
        return new L03BMorphologyMeasurement(
            span,
            view.Side,
            view.StepBlocks,
            Quantile(gradients, .5),
            Quantile(curvatures, .5),
            Quantile(curvatures, .9),
            GradientAnisotropy(residual),
            CountProminentExtrema(smooth, true, nominalIncrement),
            CountProminentExtrema(smooth, false, nominalIncrement),
            BestRingLift(smooth, 14, 26),
            craterLift,
            coneDrop,
            HighFlatFraction(residual, nominalIncrement));
    }

    private static L03BPureLandscapeView? TrySample(
        LandscapeModel model,
        LandscapeCellProfile cell,
        AtlasSite site,
        int seed,
        int side,
        double spanBlocks)
    {
        var pixels = new List<L03BEvidencePixel>(checked(side * side));
        var coordinates = new HashSet<(long X, long Z)>();
        for (int row = 0; row < side; row++)
        {
            for (int column = 0; column < side; column++)
            {
                long x = checked((long)Math.Round(site.X + (((column / (double)(side - 1)) - .5d) * spanBlocks), MidpointRounding.AwayFromZero));
                long z = checked((long)Math.Round(site.Z + (((row / (double)(side - 1)) - .5d) * spanBlocks), MidpointRounding.AwayFromZero));
                if (!coordinates.Add((x, z)))
                {
                    throw new InvalidOperationException("Evidence view scale collapsed distinct pixels onto one block coordinate.");
                }

                LandscapeSample sample = model.Sample(x, z);
                if (sample.DominantCellId != cell.CellId || sample.DominantFamily != cell.Family ||
                    sample.IsTransition || sample.ActiveResidualContributorCount != 1 ||
                    sample.PrimaryResidualWeight != 1d || sample.ForeignResidualWeight != 0d ||
                    sample.ForeignResidualContributionNormalized != 0d)
                {
                    return null;
                }
                pixels.Add(new L03BEvidencePixel(row, column, x, z, sample));
            }
        }

        var view = new L03BPureLandscapeView(
            cell.Family,
            seed,
            cell.CellId,
            site.X,
            site.Z,
            side,
            spanBlocks,
            spanBlocks / (side - 1),
            pixels.AsReadOnly());
        RequirePure(view);
        return view;
    }

    private static double[,] Detrend(double[,] values)
    {
        int side = values.GetLength(0);
        double mean = values.Cast<double>().Average();
        double meanCoordinate = (side - 1) / 2d;
        double variance = Enumerable.Range(0, side).Sum(value => (value - meanCoordinate) * (value - meanCoordinate)) * side;
        double slopeX = 0;
        double slopeZ = 0;
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            slopeX += (x - meanCoordinate) * (values[z, x] - mean);
            slopeZ += (z - meanCoordinate) * (values[z, x] - mean);
        }
        slopeX /= variance;
        slopeZ /= variance;
        var residual = new double[side, side];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            residual[z, x] = values[z, x] - mean - (slopeX * (x - meanCoordinate)) - (slopeZ * (z - meanCoordinate));
        }
        return residual;
    }

    private static double[] GradientMagnitudes(double[,] values)
    {
        var result = new List<double>();
        for (int z = 1; z < values.GetLength(0) - 1; z++) for (int x = 1; x < values.GetLength(1) - 1; x++)
        {
            (double gx, double gz) = Gradient(values, x, z);
            result.Add(Math.Sqrt((gx * gx) + (gz * gz)));
        }
        return result.ToArray();
    }

    private static double[] Curvatures(double[,] values)
    {
        var result = new List<double>();
        for (int z = 1; z < values.GetLength(0) - 1; z++) for (int x = 1; x < values.GetLength(1) - 1; x++)
        {
            result.Add(Math.Abs(values[z, x - 1] + values[z, x + 1] + values[z - 1, x] + values[z + 1, x] - (4 * values[z, x])));
        }
        return result.ToArray();
    }

    private static double GradientAnisotropy(double[,] values)
    {
        double xx = 0; double zz = 0; double xz = 0;
        for (int z = 1; z < values.GetLength(0) - 1; z++) for (int x = 1; x < values.GetLength(1) - 1; x++)
        {
            (double gx, double gz) = Gradient(values, x, z);
            xx += gx * gx; zz += gz * gz; xz += gx * gz;
        }
        double trace = xx + zz;
        return trace == 0 ? 0 : Math.Sqrt(((xx - zz) * (xx - zz)) + (4 * xz * xz)) / trace;
    }

    private static double HighFlatFraction(double[,] values, double maximumGradient)
    {
        double high = Quantile(values.Cast<double>(), .6);
        int flat = 0; int count = 0;
        for (int z = 1; z < values.GetLength(0) - 1; z++) for (int x = 1; x < values.GetLength(1) - 1; x++)
        {
            if (values[z, x] < high) continue;
            (double gx, double gz) = Gradient(values, x, z);
            if (Math.Sqrt((gx * gx) + (gz * gz)) <= maximumGradient) flat++;
            count++;
        }
        return count == 0 ? 0 : flat / (double)count;
    }

    private static (double X, double Z) Gradient(double[,] values, int x, int z) =>
        ((values[z, x + 1] - values[z, x - 1]) / 2d, (values[z + 1, x] - values[z - 1, x]) / 2d);

    private static double[,] BoxBlur(double[,] values)
    {
        var result = new double[values.GetLength(0), values.GetLength(1)];
        for (int z = 0; z < values.GetLength(0); z++) for (int x = 0; x < values.GetLength(1); x++)
        {
            double total = 0;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                total += values[Math.Clamp(z + dz, 0, values.GetLength(0) - 1), Math.Clamp(x + dx, 0, values.GetLength(1) - 1)];
            }
            result[z, x] = total / 9d;
        }
        return result;
    }

    private static int CountProminentExtrema(double[,] values, bool maxima, double threshold)
    {
        int count = 0;
        const int radius = 4;
        for (int z = radius; z < values.GetLength(0) - radius; z++) for (int x = radius; x < values.GetLength(1) - radius; x++)
        {
            double center = values[z, x]; double ring = 0; int ringCount = 0; bool extremum = true;
            for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                double other = values[z + dz, x + dx];
                if ((maxima && other >= center) || (!maxima && other <= center)) extremum = false;
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == radius) { ring += other; ringCount++; }
            }
            double prominence = maxima ? center - (ring / ringCount) : (ring / ringCount) - center;
            if (extremum && prominence > threshold) count++;
        }
        return count;
    }

    private static double BestRingLift(double[,] values, int minimumRadius, int maximumRadius)
    {
        double best = double.NegativeInfinity;
        for (int z = maximumRadius; z < values.GetLength(0) - maximumRadius; z++) for (int x = maximumRadius; x < values.GetLength(1) - maximumRadius; x++)
        {
            for (int radius = minimumRadius; radius <= maximumRadius; radius++)
            {
                best = Math.Max(best, RingMean(values, x, z, radius) - values[z, x]);
            }
        }
        return best;
    }

    private static (double CraterLift, double ConeDrop) BestNestedRelief(double[,] values, int rimRadius, int outerRadius)
    {
        (double CraterLift, double ConeDrop) best = (double.NegativeInfinity, double.NegativeInfinity);
        double bestScore = double.NegativeInfinity;
        for (int z = outerRadius; z < values.GetLength(0) - outerRadius; z++) for (int x = outerRadius; x < values.GetLength(1) - outerRadius; x++)
        {
            double rim = RingMean(values, x, z, rimRadius);
            double craterLift = rim - values[z, x];
            double coneDrop = rim - RingMean(values, x, z, outerRadius);
            double score = Math.Min(craterLift, coneDrop);
            if (score > bestScore) { bestScore = score; best = (craterLift, coneDrop); }
        }
        return best;
    }

    private static double RingMean(double[,] values, int x, int z, int radius)
    {
        double total = 0; int count = 0;
        for (int dz = -radius - 1; dz <= radius + 1; dz++) for (int dx = -radius - 1; dx <= radius + 1; dx++)
        {
            double distance = Math.Sqrt((dx * dx) + (dz * dz));
            if (Math.Abs(distance - radius) <= .75d) { total += values[z + dz, x + dx]; count++; }
        }
        return total / count;
    }

    private static double Quantile(IEnumerable<double> values, double probability)
    {
        double[] ordered = values.Order().ToArray();
        return ordered[(int)Math.Round(probability * (ordered.Length - 1))];
    }

    private sealed class StableIdComparer : IComparer<StableId>
    {
        internal static StableIdComparer Instance { get; } = new();
        public int Compare(StableId left, StableId right)
        {
            int high = left.High.CompareTo(right.High);
            return high != 0 ? high : left.Low.CompareTo(right.Low);
        }
    }
}

internal sealed record L03BPureLandscapeView(
    LandscapeFamily Family,
    int Seed,
    StableId OwnerCellId,
    long CenterX,
    long CenterZ,
    int Side,
    double SpanBlocks,
    double StepBlocks,
    IReadOnlyList<L03BEvidencePixel> Pixels);

internal sealed record L03BEvidencePixel(
    int Row,
    int Column,
    long X,
    long Z,
    LandscapeSample Sample);

internal sealed record L03BMorphologyMeasurement(
    double Span,
    int SampleSide,
    double StepBlocks,
    double MedianGradient,
    double MedianCurvature,
    double Curvature90,
    double GradientAnisotropy,
    int ProminentPeaks,
    int ProminentValleys,
    double BroadRimLift,
    double CraterLift,
    double ConeDrop,
    double HighFlatFraction)
{
    internal double NominalReliefIncrement => Span / SampleSide;
    internal double PhysicalSlope => MedianGradient / StepBlocks;
    internal double PhysicalCurvature => MedianCurvature / (StepBlocks * StepBlocks);
}
