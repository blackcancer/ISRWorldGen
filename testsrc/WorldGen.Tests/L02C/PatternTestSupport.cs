using ISRWorldGen.Core.Atlas.Profiles;

namespace ISRWorldGen.Tests.L02C;

internal static class PatternTestSupport
{
    internal const int Size = 32;

    internal static PatternDiagnosticPolicy CalibratedPolicy() => new(
        "l02c-laboratory-review-v1",
        policyVersion: 1,
        edgeGradientThreshold: 2_000,
        edgeAlignmentThresholdPpm: 200_000,
        periodicityThresholdPpm: 650_000,
        minimumPeriodLag: 4,
        maximumPeriodLag: 16);

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
                int cellX = owner % 4;
                int cellZ = owner / 4;
                values[(z * Size) + x] = checked((ushort)(4_000 + (cellX * 11_000) + (cellZ * 5_000)));
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
        // Deliberately over-regular 4x4 site lattice: nearest-site ownership forms a visible,
        // periodic square Voronoi tessellation and is therefore a positive detector witness.
        int[] owners = new int[Size * Size];
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                long bestDistance = long.MaxValue;
                int bestOwner = -1;
                for (int siteZ = 0; siteZ < 4; siteZ++)
                {
                    for (int siteX = 0; siteX < 4; siteX++)
                    {
                        int owner = (siteZ * 4) + siteX;
                        long deltaX = x - ((siteX * 8) + 4);
                        long deltaZ = z - ((siteZ * 8) + 4);
                        long distance = (deltaX * deltaX) + (deltaZ * deltaZ);
                        if (distance < bestDistance || (distance == bestDistance && owner < bestOwner))
                        {
                            bestDistance = distance;
                            bestOwner = owner;
                        }
                    }
                }

                owners[(z * Size) + x] = bestOwner;
            }
        }

        return owners;
    }

    internal static PatternInspectionMaps Maps(IReadOnlyList<ushort> final, IReadOnlyList<ushort> edges) =>
        ProfileTestSupport.Success(PatternInspectionMaps.Create(
            Raster(Size, Size, final),
            Raster(Size, Size, edges)));

    internal static InspectionRaster Raster(int width, int height, IReadOnlyList<ushort> values) =>
        ProfileTestSupport.Success(InspectionRaster.Create(width, height, values));

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
