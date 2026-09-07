using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISRWorldGen.Tests.L03A;

internal static class L03ATestSupport
{
    internal const string ConfigHash = "9329c442b47a7f215e2273287d14324ffb14047f3a126c0ace815a0aa6f602f4";
    internal const string AssetHash = "cfd370ef8c6540792e17507897d0db5a9d96b621a805d1ccc605abcf89460ddf";

    internal static readonly int[] QuickSeeds =
    [
        0, 1, -1, int.MinValue, int.MaxValue, 42, 73, 20260906,
        -437287116, -1587986303, 1571057779, -1837489763,
        720496888, 1481968978, -307755313, 60159686,
    ];

    internal static GenerationIdentity Identity(int seed = 73) => new(
        seed,
        GenerationIdentity.SupportedAlgorithmVersion,
        GenerationIdentity.SupportedSchemaVersion,
        Hash256.Parse(ConfigHash),
        Hash256.Parse(AssetHash),
        "l03a-plates-continents-v1");

    internal static StableId PlateId(ulong index) =>
        StableId.Derive(RandomDomain.Geology, StableId.Zero, index);

    internal static T Success<T>(GenerationResult<T> result)
        where T : class
    {
        Assert.IsInstanceOfType<GenerationSuccess<T>>(result);
        return ((GenerationSuccess<T>)result).Snapshot;
    }

    internal static ContinentalFieldSettings ContinentalSettings() => new(
        macroFeatureCount: 5,
        regionalFeatureCount: 18,
        maximumFeatureCount: 64,
        maximumRasterSamples: 1_000_000);

    internal static WorldBounds VastBounds() => new(0, 0, 12_288, 8_192);

    internal static IReadOnlyList<int> SeedCorpus(string propertyName)
    {
        string path = Path.Combine(FindRepositoryRoot(), "registry", "fixtures.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty(propertyName)
            .EnumerateArray()
            .Select(item => item.GetInt32())
            .ToArray();
    }

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

    internal static byte[] RenderTopologyBitmap(ContinentalRaster raster)
    {
        int stride = checked(((raster.Width * 3) + 3) & ~3);
        int imageBytes = checked(stride * raster.Height);
        byte[] bitmap = new byte[checked(54 + imageBytes)];
        bitmap[0] = (byte)'B';
        bitmap[1] = (byte)'M';
        WriteInt32(bitmap, 2, bitmap.Length);
        WriteInt32(bitmap, 10, 54);
        WriteInt32(bitmap, 14, 40);
        WriteInt32(bitmap, 18, raster.Width);
        WriteInt32(bitmap, 22, raster.Height);
        bitmap[26] = 1;
        bitmap[28] = 24;
        WriteInt32(bitmap, 34, imageBytes);

        for (int z = 0; z < raster.Height; z++)
        {
            int row = 54 + ((raster.Height - 1 - z) * stride);
            for (int x = 0; x < raster.Width; x++)
            {
                bool land = raster.IsLand(x, z);
                bool coast = land &&
                    ((x > 0 && !raster.IsLand(x - 1, z)) ||
                     (x + 1 < raster.Width && !raster.IsLand(x + 1, z)) ||
                     (z > 0 && !raster.IsLand(x, z - 1)) ||
                     (z + 1 < raster.Height && !raster.IsLand(x, z + 1)));
                int offset = row + (x * 3);
                if (coast)
                {
                    bitmap[offset] = 235;
                    bitmap[offset + 1] = 245;
                    bitmap[offset + 2] = 245;
                }
                else if (land)
                {
                    int height = Math.Clamp(raster.HeightPpm[(z * raster.Width) + x] / 7_000, 0, 100);
                    bitmap[offset] = checked((byte)(35 + (height / 4)));
                    bitmap[offset + 1] = checked((byte)(105 + height));
                    bitmap[offset + 2] = checked((byte)(55 + (height / 2)));
                }
                else
                {
                    int depth = Math.Clamp(-raster.HeightPpm[(z * raster.Width) + x] / 8_000, 0, 100);
                    bitmap[offset] = checked((byte)(155 + depth));
                    bitmap[offset + 1] = checked((byte)(80 + (depth / 3)));
                    bitmap[offset + 2] = checked((byte)(20 + (depth / 5)));
                }
            }
        }

        return bitmap;
    }

    internal static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static void WriteInt32(byte[] destination, int offset, int value)
    {
        destination[offset] = unchecked((byte)value);
        destination[offset + 1] = unchecked((byte)(value >> 8));
        destination[offset + 2] = unchecked((byte)(value >> 16));
        destination[offset + 3] = unchecked((byte)(value >> 24));
    }
}
