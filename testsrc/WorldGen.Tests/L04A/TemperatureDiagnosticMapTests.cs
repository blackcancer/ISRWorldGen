using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L04A;

[TestClass]
public sealed class TemperatureDiagnosticMapTests
{
    private const string AnalyticGridId = "l04a-analytic-grid-v1";
    private const int Side = 96;
    // 95 exact 180-L intervals keep the two poleward witnesses equidistant from the equator.
    private const long MinimumCoordinate = -8_550;
    private const long CoordinateStep = 180;
    private static readonly LatitudeAxis NorthSouth = new(new WorldBlockPosition(0, 0), 0d, 1d, 100d);
    private static readonly TemperatureSettings Settings = new(30d, 0.4d, 0.006d, -4d);

    [TestMethod]
    [DoNotParallelize]
    public void T04_01_DiagnosticMapsRecordFrozenTemperatureFieldsAndNumericOracle()
    {
        string output = Path.Combine(FindRepositoryRoot(), ".local", "L04A", "maps", "T04-01", AnalyticGridId);
        Directory.CreateDirectory(output);

        TemperatureSample[,] samples = BuildFixture();
        AssertGradientOracle(samples);

        byte[] referencePng = RenderPng(samples, sample => sample.ReferenceCelsius);
        byte[] surfacePng = RenderPng(samples, sample => sample.SurfaceCelsius);
        CollectionAssert.AreEqual(referencePng, RenderPng(BuildFixture(), sample => sample.ReferenceCelsius),
            "The fixed diagnostic palette and raster must be reproducible.");
        Assert.IsTrue(referencePng.AsSpan(1, 3).SequenceEqual(new byte[] { 80, 78, 71 }));
        Assert.IsTrue(surfacePng.AsSpan(1, 3).SequenceEqual(new byte[] { 80, 78, 71 }));

        string referencePath = Path.Combine(output, "temperature-reference.png");
        string surfacePath = Path.Combine(output, "temperature-surface.png");
        string oraclePath = Path.Combine(output, "oracle.json");
        string manifestPath = Path.Combine(output, "manifest.json");
        WriteAtomic(referencePath, referencePng);
        WriteAtomic(surfacePath, surfacePng);
        byte[] oracle = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            testId = "T04-01",
            status = "PASS",
            fixture = AnalyticGridId,
            assertions = new
            {
                latitudeSymmetry = "PASS",
                altitudeCorrection = "PASS",
                continentalityOffset = "PASS",
                finiteSamples = "PASS",
                repeatedRasterHash = "PASS",
            },
        }, JsonOptions);
        WriteAtomic(oraclePath, oracle);

        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            testId = "T04-01",
            status = "PASS",
            commit = Environment.GetEnvironmentVariable("ISR_L04A_EVIDENCE_COMMIT") ?? "WORKING_TREE",
            algorithmVersion = TemperatureField.AlgorithmVersion,
            seed = "not-applicable: deterministic analytical grid contains no random draw",
            profile = AnalyticGridId,
            projection = "orthogonal model X/Z; latitude axis +Z; origin at equator",
            dimensions = new { width = Side, height = Side },
            extentModelLength = new
            {
                minX = MinimumCoordinate,
                maxX = MinimumCoordinate + ((long)(Side - 1) * CoordinateStep),
                minZ = MinimumCoordinate,
                maxZ = MinimumCoordinate + ((long)(Side - 1) * CoordinateStep),
                sampleStep = CoordinateStep,
            },
            parameters = new
            {
                equatorialReferenceCelsius = Settings.EquatorialReferenceCelsius,
                polewardCoolingCelsiusPerDegree = Settings.PolewardCoolingCelsiusPerDegree,
                altitudeLapseCelsiusPerModelLength = Settings.AltitudeLapseCelsiusPerModelLength,
                inlandReferenceOffsetCelsius = Settings.InlandReferenceOffsetCelsius,
                modelLengthPerDegree = NorthSouth.ModelLengthPerDegree,
                analyticAltitude = "radial hill, 0..1200 L",
                analyticContinentality = "west coast 0..east inland 1",
            },
            palette = new
            {
                id = "l04a-temperature-linear-v1",
                minimumCelsius = -20d,
                maximumCelsius = 30d,
                belowMinimum = "#172554",
                minimum = "#2563eb",
                midpoint = "#f8fafc",
                maximum = "#dc2626",
                aboveMaximum = "#7f1d1d",
                missing = "#ff00ff",
                interpolation = "none; fixed per-value linear RGB mapping",
            },
            layers = new[]
            {
                Layer("temperature-reference", referencePath, referencePng, "degrees Celsius model", samples, sample => sample.ReferenceCelsius),
                Layer("temperature-surface", surfacePath, surfacePng, "degrees Celsius model", samples, sample => sample.SurfaceCelsius),
            },
            oracle = new { file = Path.GetFileName(oraclePath), sha256 = Sha256(oracle), status = "PASS" },
        }, JsonOptions);
        WriteAtomic(manifestPath, manifest);

        Assert.IsTrue(new[] { referencePath, surfacePath, oraclePath, manifestPath }.All(File.Exists));
        Assert.AreEqual(Sha256(referencePng), Sha256(File.ReadAllBytes(referencePath)));
        Assert.AreEqual(Sha256(surfacePng), Sha256(File.ReadAllBytes(surfacePath)));
    }

    private static TemperatureSample[,] BuildFixture()
    {
        var samples = new TemperatureSample[Side, Side];
        for (int z = 0; z < Side; z++)
        {
            for (int x = 0; x < Side; x++)
            {
                long modelX = MinimumCoordinate + ((long)x * CoordinateStep);
                long modelZ = MinimumCoordinate + ((long)z * CoordinateStep);
                double normalizedX = x / (double)(Side - 1);
                double normalizedZ = z / (double)(Side - 1);
                double distance = Math.Sqrt(Math.Pow((normalizedX - .5d) * 2d, 2d) + Math.Pow((normalizedZ - .5d) * 2d, 2d));
                double altitude = Math.Max(0d, 1_200d * (1d - distance));
                samples[z, x] = TemperatureField.Calculate(NorthSouth, Settings,
                    new TemperatureInput(new WorldBlockPosition(modelX, modelZ), altitude, normalizedX));
            }
        }

        return samples;
    }

    private static void AssertGradientOracle(TemperatureSample[,] samples)
    {
        IEnumerable<TemperatureSample> all = samples.Cast<TemperatureSample>();
        Assert.IsTrue(all.All(sample => double.IsFinite(sample.ReferenceCelsius) && double.IsFinite(sample.SurfaceCelsius)));
        int center = Side / 2;
        Assert.IsLessThan(samples[center, 0].ReferenceCelsius, samples[center, Side - 1].ReferenceCelsius,
            "Continentality must alter the declared reference field from coast to inland.");
        Assert.IsGreaterThan(samples[0, center].ReferenceCelsius, samples[center, center].ReferenceCelsius,
            "Equatorial reference temperature must exceed the poleward witness.");
        Assert.IsLessThan(samples[center, center].ReferenceCelsius, samples[center, center].SurfaceCelsius,
            "The radial mountain witness must apply exactly one positive altitude correction.");
        Assert.AreEqual(samples[0, center].ReferenceCelsius, samples[Side - 1, center].ReferenceCelsius, 1e-12,
            "The fixed North-South axis must keep equal-distance north/south witnesses symmetric.");
    }

    private static object Layer(
        string name,
        string path,
        byte[] png,
        string unit,
        TemperatureSample[,] samples,
        Func<TemperatureSample, double> selector) => new
    {
        name,
        file = Path.GetFileName(path),
        unit,
        missingValue = "none",
        observedMinimum = samples.Cast<TemperatureSample>().Select(selector).Min(),
        observedMaximum = samples.Cast<TemperatureSample>().Select(selector).Max(),
        sha256 = Sha256(png),
        format = "PNG lossless RGBA",
    };

    private static byte[] RenderPng(TemperatureSample[,] samples, Func<TemperatureSample, double> selector)
    {
        byte[] raw = new byte[Side * ((Side * 4) + 1)];
        int offset = 0;
        for (int z = 0; z < Side; z++)
        {
            raw[offset++] = 0; // PNG filter: None; no smoothing or interpolation across samples.
            for (int x = 0; x < Side; x++)
            {
                (byte red, byte green, byte blue) = Palette(selector(samples[z, x]));
                raw[offset++] = red;
                raw[offset++] = green;
                raw[offset++] = blue;
                raw[offset++] = 255;
            }
        }

        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(output, "IHDR"u8, BuildHeader());
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(raw);
        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static byte[] BuildHeader()
    {
        byte[] header = new byte[13];
        WriteBigEndian(header, 0, Side);
        WriteBigEndian(header, 4, Side);
        header[8] = 8;
        header[9] = 6;
        return header;
    }

    private static (byte Red, byte Green, byte Blue) Palette(double value)
    {
        if (!double.IsFinite(value)) return (255, 0, 255);
        if (value <= -20d) return (23, 37, 84);
        if (value >= 30d) return (127, 29, 29);
        double normalized = (value + 20d) / 50d;
        return normalized <= .5d
            ? Mix((37, 99, 235), (248, 250, 252), normalized * 2d)
            : Mix((248, 250, 252), (220, 38, 38), (normalized - .5d) * 2d);
    }

    private static (byte Red, byte Green, byte Blue) Mix((int Red, int Green, int Blue) from, (int Red, int Green, int Blue) to, double factor) =>
        ((byte)Math.Round(from.Red + ((to.Red - from.Red) * factor)),
         (byte)Math.Round(from.Green + ((to.Green - from.Green) * factor)),
         (byte)Math.Round(from.Blue + ((to.Blue - from.Blue) * factor)));

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);
        byte[] crcInput = new byte[type.Length + data.Length];
        type.CopyTo(crcInput);
        data.CopyTo(crcInput, type.Length);
        uint crc = Crc32(crcInput);
        WriteBigEndian(length, 0, unchecked((int)crc));
        output.Write(length);
    }

    private static void WriteBigEndian(Span<byte> destination, int offset, int value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> content)
    {
        uint crc = 0xffff_ffffu;
        foreach (byte value in content)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1u) == 0u ? 0u : 0xedb8_8320u);
        }

        return ~crc;
    }

    private static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static void WriteAtomic(string path, byte[] content)
    {
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string gitMarker = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root for L04-A diagnostic artifacts.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
