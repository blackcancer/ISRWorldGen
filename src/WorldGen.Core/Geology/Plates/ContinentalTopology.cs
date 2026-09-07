using System.Collections.ObjectModel;
using System.Numerics;
using ISRWorldGen.Core.Atlas.Geometry;

namespace ISRWorldGen.Core.Geology.Plates;

public readonly record struct ContinentalContourSegment(ExactPoint Start, ExactPoint End);

public sealed class ContinentalTopologyReport
{
    internal ContinentalTopologyReport(
        int landSampleCount,
        int openOceanSampleCount,
        int inlandBasinSampleCount,
        IEnumerable<int> landComponentAreas,
        int openOceanComponentCount,
        IEnumerable<int> inlandBasinComponentAreas,
        int coastlineSegmentCount,
        int coastlineBoundaryCellCount,
        int coastlineCornerCount,
        int longestStraightCoastRun,
        IEnumerable<ContinentalContourSegment> coastlineSegments)
    {
        LandSampleCount = landSampleCount;
        OpenOceanSampleCount = openOceanSampleCount;
        InlandBasinSampleCount = inlandBasinSampleCount;
        LandComponentAreas = Array.AsReadOnly(landComponentAreas.Order().ToArray());
        OpenOceanComponentCount = openOceanComponentCount;
        InlandBasinComponentAreas = Array.AsReadOnly(inlandBasinComponentAreas.Order().ToArray());
        CoastlineSegmentCount = coastlineSegmentCount;
        CoastlineBoundaryCellCount = coastlineBoundaryCellCount;
        CoastlineCornerCount = coastlineCornerCount;
        LongestStraightCoastRun = longestStraightCoastRun;
        CoastlineSegments = Array.AsReadOnly(coastlineSegments.ToArray());
    }

    public int LandSampleCount { get; }

    public int OpenOceanSampleCount { get; }

    public int InlandBasinSampleCount { get; }

    public ReadOnlyCollection<int> LandComponentAreas { get; }

    public int OpenOceanComponentCount { get; }

    public ReadOnlyCollection<int> InlandBasinComponentAreas { get; }

    public int CoastlineSegmentCount { get; }

    public int CoastlineBoundaryCellCount { get; }

    public int CoastlineCornerCount { get; }

    public int LongestStraightCoastRun { get; }

    public ReadOnlyCollection<ContinentalContourSegment> CoastlineSegments { get; }
}

public static class ContinentalTopologyAnalyzer
{
    public static ContinentalTopologyReport Analyze(ContinentalRaster raster)
    {
        ArgumentNullException.ThrowIfNull(raster);
        bool[] visited = new bool[raster.HeightPpm.Count];
        var landAreas = new List<int>();
        var basinAreas = new List<int>();
        int openOceanCount = 0;
        int inlandBasinCount = 0;
        int openOceanComponents = 0;

        for (int z = 0; z < raster.Height; z++)
        {
            for (int x = 0; x < raster.Width; x++)
            {
                int index = (z * raster.Width) + x;
                if (visited[index])
                {
                    continue;
                }

                bool land = raster.IsLand(x, z);
                (int area, bool touchesBoundary) = Flood(raster, x, z, land, visited);
                if (land)
                {
                    landAreas.Add(area);
                }
                else if (touchesBoundary)
                {
                    openOceanCount += area;
                    openOceanComponents++;
                }
                else
                {
                    inlandBasinCount += area;
                    basinAreas.Add(area);
                }
            }
        }

        int landCount = landAreas.Sum();
        var boundaryLandCells = new HashSet<int>();
        int corners = 0;
        for (int z = 0; z < raster.Height; z++)
        {
            for (int x = 0; x < raster.Width; x++)
            {
                if (!raster.IsLand(x, z))
                {
                    continue;
                }

                bool horizontal = IsWater(raster, x - 1, z) || IsWater(raster, x + 1, z);
                bool vertical = IsWater(raster, x, z - 1) || IsWater(raster, x, z + 1);
                if (horizontal || vertical)
                {
                    boundaryLandCells.Add((z * raster.Width) + x);
                }

                if (horizontal && vertical)
                {
                    corners++;
                }
            }
        }

        int segments = 0;
        int longestRun = 0;
        var contourSegments = new List<ContinentalContourSegment>();
        for (int z = 0; z < raster.Height - 1; z++)
        {
            int run = 0;
            for (int x = 0; x < raster.Width; x++)
            {
                if (raster.IsLand(x, z) != raster.IsLand(x, z + 1))
                {
                    segments++;
                    run++;
                    longestRun = Math.Max(longestRun, run);
                    contourSegments.Add(HorizontalContour(raster.Grid, x, z));
                }
                else
                {
                    run = 0;
                }
            }
        }

        for (int x = 0; x < raster.Width - 1; x++)
        {
            int run = 0;
            for (int z = 0; z < raster.Height; z++)
            {
                if (raster.IsLand(x, z) != raster.IsLand(x + 1, z))
                {
                    segments++;
                    run++;
                    longestRun = Math.Max(longestRun, run);
                    contourSegments.Add(VerticalContour(raster.Grid, x, z));
                }
                else
                {
                    run = 0;
                }
            }
        }

        return new ContinentalTopologyReport(
            landCount,
            openOceanCount,
            inlandBasinCount,
            landAreas,
            openOceanComponents,
            basinAreas,
            segments,
            boundaryLandCells.Count,
            corners,
            longestRun,
            contourSegments);
    }

    private static (int Area, bool TouchesBoundary) Flood(
        ContinentalRaster raster,
        int startX,
        int startZ,
        bool land,
        bool[] visited)
    {
        var queue = new Queue<int>();
        int start = (startZ * raster.Width) + startX;
        queue.Enqueue(start);
        visited[start] = true;
        int area = 0;
        bool touchesBoundary = false;
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % raster.Width;
            int z = index / raster.Width;
            area++;
            touchesBoundary |= x == 0 || z == 0 || x == raster.Width - 1 || z == raster.Height - 1;
            Visit(x - 1, z);
            Visit(x + 1, z);
            Visit(x, z - 1);
            Visit(x, z + 1);
        }

        return (area, touchesBoundary);

        void Visit(int x, int z)
        {
            if (x < 0 || z < 0 || x >= raster.Width || z >= raster.Height)
            {
                return;
            }

            int index = (z * raster.Width) + x;
            if (visited[index] || raster.IsLand(x, z) != land)
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue(index);
        }
    }

    private static bool IsWater(ContinentalRaster raster, int x, int z) =>
        x >= 0 && z >= 0 && x < raster.Width && z < raster.Height && !raster.IsLand(x, z);

    private static ContinentalContourSegment HorizontalContour(ContinentalSamplingGrid grid, int x, int z)
    {
        BigInteger sampleX = (BigInteger)grid.OriginX + ((BigInteger)x * grid.StepX);
        BigInteger firstZ = (BigInteger)grid.OriginZ + ((BigInteger)z * grid.StepZ);
        BigInteger secondZ = firstZ + grid.StepZ;
        BigInteger midpointZ = firstZ + secondZ;
        return new ContinentalContourSegment(
            new ExactPoint(
                new ExactRational((2 * sampleX) - grid.StepX, 2),
                new ExactRational(midpointZ, 2)),
            new ExactPoint(
                new ExactRational((2 * sampleX) + grid.StepX, 2),
                new ExactRational(midpointZ, 2)));
    }

    private static ContinentalContourSegment VerticalContour(ContinentalSamplingGrid grid, int x, int z)
    {
        BigInteger firstX = (BigInteger)grid.OriginX + ((BigInteger)x * grid.StepX);
        BigInteger secondX = firstX + grid.StepX;
        BigInteger midpointX = firstX + secondX;
        BigInteger sampleZ = (BigInteger)grid.OriginZ + ((BigInteger)z * grid.StepZ);
        return new ContinentalContourSegment(
            new ExactPoint(
                new ExactRational(midpointX, 2),
                new ExactRational((2 * sampleZ) - grid.StepZ, 2)),
            new ExactPoint(
                new ExactRational(midpointX, 2),
                new ExactRational((2 * sampleZ) + grid.StepZ, 2)));
    }
}
