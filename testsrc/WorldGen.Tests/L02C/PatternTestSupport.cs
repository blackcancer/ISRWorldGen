using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02C;

internal static class PatternTestSupport
{
    internal const int Size = 32;

    internal static PatternInspectionBudget InspectionBudget() => new(
        maximumPixelsPerRaster: 1_048_576,
        maximumBytesPerRaster: 2_097_216,
        maximumMapPairBytes: 4_194_432);

    internal static PatternDiagnosticPolicy CalibratedPolicy() => new(
        "l02c-laboratory-review-v1",
        policyVersion: 1,
        edgeGradientThreshold: 2_000,
        edgeAlignmentThresholdPpm: 200_000,
        periodicityThresholdPpm: 450_000,
        minimumPeriodLag: 4,
        maximumPeriodLag: 16,
        maximumAnalysisWorkUnits: 100_000,
        maximumCorpusAnalysisWorkUnits: 500_000_000);

    internal static PatternInspectionMaps VisibleVoronoiWitness() =>
        Maps(CreateVisibleFinalValues(), CreateGridEdges());

    internal static PatternInspectionMaps LowContrastWitness()
    {
        ushort[] values = CreateVisibleFinalValues();
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = checked((ushort)(30_000 + (values[index] / 256)));
        }

        return Maps(values, CreateGridEdges());
    }

    internal static PatternInspectionMaps SmoothField()
    {
        ushort[] values = new ushort[Size * Size];
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                values[(z * Size) + x] = checked((ushort)(1_000 + (x * 200) + (z * 100)));
            }
        }

        return Maps(values, CreateGridEdges());
    }

    internal static PatternInspectionMaps PeriodicNonVoronoi()
    {
        ushort[] values = new ushort[Size * Size];
        ushort[] edges = new ushort[Size * Size];
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                values[(z * Size) + x] = ((x / 4) & 1) == 0 ? (ushort)8_000 : (ushort)52_000;
            }
        }

        return Maps(values, edges);
    }

    internal static ushort[] CreateVisibleFinalValues()
    {
        int[] owners = CreateRegularVoronoiOwners();
        ushort[] values = new ushort[Size * Size];
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                int owner = owners[(z * Size) + x];
                values[(z * Size) + x] = checked((ushort)(4_000 + (owner * 3_500)));
            }
        }

        return values;
    }

    internal static ushort[] CreateGridEdges()
    {
        int[] owners = CreateRegularVoronoiOwners();
        ushort[] values = new ushort[Size * Size];
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                int index = (z * Size) + x;
                if ((x > 0 && owners[index] != owners[index - 1]) ||
                    (z > 0 && owners[index] != owners[index - Size]))
                {
                    values[index] = ushort.MaxValue;
                }
            }
        }

        return values;
    }

    private static int[] CreateRegularVoronoiOwners()
    {
        // Deliberately over-regular 4x4 site lattice passed through the exact L02-A builder.
        // Its nearest-site field is a visible, periodic square Voronoi tessellation and is
        // therefore a positive detector witness rather than an arbitrary checkerboard.
        AtlasMesh atlas = CreateRegularVoronoiAtlas();
        int[] owners = new int[Size * Size];
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                long bestDistance = long.MaxValue;
                int bestOwner = -1;
                for (int siteIndex = 0; siteIndex < atlas.Sites.Count; siteIndex++)
                {
                    AtlasSite site = atlas.Sites[siteIndex];
                    long deltaX = x - site.X;
                    long deltaZ = z - site.Z;
                    long distance = (deltaX * deltaX) + (deltaZ * deltaZ);
                    if (distance < bestDistance || (distance == bestDistance && siteIndex < bestOwner))
                    {
                        bestDistance = distance;
                        bestOwner = siteIndex;
                    }
                }

                owners[(z * Size) + x] = bestOwner;
            }
        }

        return owners;
    }

    private static AtlasMesh CreateRegularVoronoiAtlas()
    {
        var sites = new List<AtlasSite>(16);
        for (int siteZ = 0; siteZ < 4; siteZ++)
        {
            for (int siteX = 0; siteX < 4; siteX++)
            {
                ulong index = checked((ulong)((siteZ * 4) + siteX));
                sites.Add(new AtlasSite(
                    StableId.Derive(RandomDomain.Sites, StableId.Zero, index),
                    (siteX * 8) + 4,
                    (siteZ * 8) + 4));
            }
        }

        return ProfileTestSupport.Success(AtlasGeometryBuilder.Build(
            ProfileTestSupport.Identity(),
            new WorldBounds(0, 0, Size, Size),
            sites,
            new AtlasGeometryBuildOptions(1, GeometryCacheMode.Cold)));
    }

    internal static PatternInspectionMaps Maps(IReadOnlyList<ushort> final, IReadOnlyList<ushort> edges) =>
        ProfileTestSupport.Success(PatternInspectionMaps.Create(
            Raster(Size, Size, final),
            Raster(Size, Size, edges),
            InspectionBudget()));

    internal static InspectionRaster Raster(int width, int height, IReadOnlyList<ushort> values) =>
        ProfileTestSupport.Success(InspectionRaster.Create(width, height, values, InspectionBudget()));

    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ISRWorldGen.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
