using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISRWorldGen.Tests.L03A;

internal enum PlateReviewLayer
{
    Plate = 0,
    Crust = 1,
    BoundaryField = 2,
    ContinentalAtlasOverlay = 3,
}

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

    internal static GenerationIdentity Identity(int seed, FrozenScaleProfile profile) => new(
        seed,
        GenerationIdentity.SupportedAlgorithmVersion,
        GenerationIdentity.SupportedSchemaVersion,
        profile.GeographyConfigHash,
        Hash256.Parse(AssetHash),
        $"l03a-{profile.Id}-v{profile.ProfileVersion}");

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

    internal static FrozenScaleProfile FrozenProfile(string id)
    {
        ScaleProfileDefinition definition = ScaleProfileCatalog.Proposals.Single(profile => profile.Id == id);
        var nativeConstraints = new NativeWorldConstraints(
            "l03a-qualified-native-v1",
            ruleSetVersion: 1,
            supportedHeights: new[] { 256, 384, 512 },
            minimumHorizontalBlocks: 4_096,
            maximumHorizontalBlocks: 1_024_000,
            horizontalStepBlocks: 512);
        return Success(ScaleProfileValidator.ValidateAndFreeze(definition, nativeConstraints));
    }

    internal static WorldBounds Bounds(FrozenScaleProfile profile)
    {
        WorldDomain domain = profile.AtlasIndexProfile.Domain;
        return new WorldBounds(
            domain.X.MinInclusive,
            domain.Z.MinInclusive,
            domain.X.MaxExclusive,
            domain.Z.MaxExclusive);
    }

    internal static ContinentalSamplingGrid ProfileGrid(FrozenScaleProfile profile, int width, int height)
    {
        long stepX = (profile.WidthBlocks / width) & ~1L;
        long stepZ = (profile.LengthBlocks / height) & ~1L;
        long originX = PositiveModulo(-(stepX / 2), profile.AtlasTileSizeBlocks);
        long originZ = PositiveModulo(-(stepZ / 2), profile.AtlasTileSizeBlocks);
        return new ContinentalSamplingGrid(originX, originZ, stepX, stepZ, width, height);
    }

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

    internal static byte[] RenderMarineBitmap(ContinentalRaster raster, ContinentalTopologyReport report) =>
        RenderBitmap(raster.Width, raster.Height, (x, z) =>
        {
            ContinentalSurfaceDomain domain = report.SurfaceDomains[(z * raster.Width) + x];
            return domain switch
            {
                ContinentalSurfaceDomain.Land => (B: (byte)55, G: (byte)165, R: (byte)75),
                ContinentalSurfaceDomain.OpenOcean => (B: (byte)220, G: (byte)95, R: (byte)25),
                ContinentalSurfaceDomain.InlandBasin => (B: (byte)210, G: (byte)210, R: (byte)35),
                _ => throw new InvalidOperationException("Unknown surface domain."),
            };
        });

    internal static byte[] RenderPlateLayerBitmap(
        AtlasMesh atlas,
        PlateAtlasSnapshot snapshot,
        ContinentalRaster raster,
        PlateReviewLayer layer)
    {
        int[] owners = RasterOwners(atlas, raster.Grid);
        var cells = snapshot.Cells.ToDictionary(cell => cell.CellId);
        return RenderBitmap(raster.Width, raster.Height, (x, z) =>
        {
            int index = (z * raster.Width) + x;
            AtlasSite owner = atlas.Sites[owners[index]];
            PlateCellState cell = cells[owner.Id];
            bool atlasEdge =
                (x > 0 && owners[index - 1] != owners[index]) ||
                (z > 0 && owners[index - raster.Width] != owners[index]);
            if (atlasEdge)
            {
                return layer == PlateReviewLayer.ContinentalAtlasOverlay
                    ? (B: (byte)210, G: (byte)35, R: (byte)235)
                    : (B: (byte)240, G: (byte)240, R: (byte)240);
            }

            return layer switch
            {
                PlateReviewLayer.Plate => StableColor(cell.PlateId),
                PlateReviewLayer.Crust => cell.CrustKind switch
                {
                    CrustKind.Oceanic => (B: (byte)190, G: (byte)75, R: (byte)25),
                    CrustKind.Transitional => (B: (byte)75, G: (byte)165, R: (byte)195),
                    CrustKind.Continental => (B: (byte)45, G: (byte)145, R: (byte)75),
                    _ => throw new InvalidOperationException("Unknown crust kind."),
                },
                PlateReviewLayer.BoundaryField =>
                    (B: checked((byte)(25 + (cell.SubsidenceNormalized * 220))),
                     G: (byte)25,
                     R: checked((byte)(25 + (cell.UpliftNormalized * 220)))),
                PlateReviewLayer.ContinentalAtlasOverlay => raster.IsLand(x, z)
                    ? (B: (byte)50, G: (byte)155, R: (byte)65)
                    : (B: (byte)205, G: (byte)85, R: (byte)25),
                _ => throw new InvalidOperationException("Unknown review layer."),
            };
        });
    }

    internal static (int CoastSegments, int AtlasAligned, int TileAligned) CoastlineAlignment(
        AtlasMesh atlas,
        ContinentalRaster raster,
        int tileSizeBlocks)
    {
        int[] owners = RasterOwners(atlas, raster.Grid);
        int coast = 0;
        int atlasAligned = 0;
        int tileAligned = 0;
        for (int z = 0; z < raster.Height; z++)
        {
            for (int x = 0; x < raster.Width - 1; x++)
            {
                int index = (z * raster.Width) + x;
                if (raster.IsLand(x, z) == raster.IsLand(x + 1, z))
                {
                    continue;
                }

                coast++;
                atlasAligned += owners[index] != owners[index + 1] ? 1 : 0;
                long firstX = checked(raster.Grid.OriginX + ((long)x * raster.Grid.StepX));
                long secondX = checked(firstX + raster.Grid.StepX);
                tileAligned += (firstX + secondX) % (2L * tileSizeBlocks) == 0 ? 1 : 0;
            }
        }

        for (int z = 0; z < raster.Height - 1; z++)
        {
            for (int x = 0; x < raster.Width; x++)
            {
                int index = (z * raster.Width) + x;
                if (raster.IsLand(x, z) == raster.IsLand(x, z + 1))
                {
                    continue;
                }

                coast++;
                atlasAligned += owners[index] != owners[index + raster.Width] ? 1 : 0;
                long firstZ = checked(raster.Grid.OriginZ + ((long)z * raster.Grid.StepZ));
                long secondZ = checked(firstZ + raster.Grid.StepZ);
                tileAligned += (firstZ + secondZ) % (2L * tileSizeBlocks) == 0 ? 1 : 0;
            }
        }

        return (coast, atlasAligned, tileAligned);
    }

    internal static ContinentalSamplingGrid CenteredGrid(
        FrozenScaleProfile profile,
        long step,
        int width,
        int height)
    {
        long spanX = checked((long)(width - 1) * step);
        long spanZ = checked((long)(height - 1) * step);
        long originX = checked((profile.WidthBlocks - spanX) / 2);
        long originZ = checked((profile.LengthBlocks - spanZ) / 2);
        return new ContinentalSamplingGrid(originX, originZ, step, step, width, height);
    }

    internal static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static byte[] RenderBitmap(
        int width,
        int height,
        Func<int, int, (byte B, byte G, byte R)> pixel)
    {
        int stride = checked(((width * 3) + 3) & ~3);
        int imageBytes = checked(stride * height);
        byte[] bitmap = new byte[checked(54 + imageBytes)];
        bitmap[0] = (byte)'B';
        bitmap[1] = (byte)'M';
        WriteInt32(bitmap, 2, bitmap.Length);
        WriteInt32(bitmap, 10, 54);
        WriteInt32(bitmap, 14, 40);
        WriteInt32(bitmap, 18, width);
        WriteInt32(bitmap, 22, height);
        bitmap[26] = 1;
        bitmap[28] = 24;
        WriteInt32(bitmap, 34, imageBytes);
        for (int z = 0; z < height; z++)
        {
            int row = 54 + ((height - 1 - z) * stride);
            for (int x = 0; x < width; x++)
            {
                (byte b, byte g, byte r) = pixel(x, z);
                int offset = row + (x * 3);
                bitmap[offset] = b;
                bitmap[offset + 1] = g;
                bitmap[offset + 2] = r;
            }
        }

        return bitmap;
    }

    private static int[] RasterOwners(AtlasMesh atlas, ContinentalSamplingGrid grid)
    {
        var owners = new int[checked(grid.Width * grid.Height)];
        for (int z = 0; z < grid.Height; z++)
        {
            long worldZ = checked(grid.OriginZ + ((long)z * grid.StepZ));
            for (int x = 0; x < grid.Width; x++)
            {
                long worldX = checked(grid.OriginX + ((long)x * grid.StepX));
                int best = 0;
                decimal dx = worldX - atlas.Sites[0].X;
                decimal dz = worldZ - atlas.Sites[0].Z;
                decimal bestDistance = (dx * dx) + (dz * dz);
                for (int site = 1; site < atlas.Sites.Count; site++)
                {
                    dx = worldX - atlas.Sites[site].X;
                    dz = worldZ - atlas.Sites[site].Z;
                    decimal distance = (dx * dx) + (dz * dz);
                    if (distance < bestDistance)
                    {
                        best = site;
                        bestDistance = distance;
                    }
                }

                owners[(z * grid.Width) + x] = best;
            }
        }

        return owners;
    }

    private static (byte B, byte G, byte R) StableColor(StableId id) =>
        (
            checked((byte)(55 + (id.Low % 170))),
            checked((byte)(55 + ((id.Low >> 16) % 170))),
            checked((byte)(55 + ((id.High >> 32) % 170))));

    private static long PositiveModulo(long value, long divisor)
    {
        long remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    private static void WriteInt32(byte[] destination, int offset, int value)
    {
        destination[offset] = unchecked((byte)value);
        destination[offset + 1] = unchecked((byte)(value >> 8));
        destination[offset + 2] = unchecked((byte)(value >> 16));
        destination[offset + 3] = unchecked((byte)(value >> 24));
    }
}
