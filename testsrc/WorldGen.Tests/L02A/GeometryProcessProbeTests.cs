using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Tests.L02A;

[TestClass]
public sealed class GeometryProcessProbeTests
{
    [TestMethod]
    public void DeterminismProcessProbe()
    {
        const int seed = -437287116;
        WorldBounds bounds = new(0, 0, 512, 384);
        GenerationIdentity identity = GeometryTestSupport.Identity(seed);
        GeneratedSiteSet generated = ((GenerationSuccess<GeneratedSiteSet>)AtlasSiteGenerator.Generate(
            identity,
            bounds,
            new AtlasSiteGenerationSettings(48))).Snapshot;
        AtlasMesh mesh = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            identity,
            bounds,
            generated.Sites.Reverse(),
            new AtlasGeometryBuildOptions(16, GeometryCacheMode.Precomputed)));
        GeometryTestSupport.AssertValidTopology(mesh);
        string geometryHash = GeometryTestSupport.Fingerprint(mesh);
        string boundaryFieldHash = BoundaryFingerprint();
        Assert.AreEqual(64, geometryHash.Length);
        Assert.AreEqual(64, boundaryFieldHash.Length);

        string? outputPath = Environment.GetEnvironmentVariable("ISRW_L02A_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        string commit = Environment.GetEnvironmentVariable("ISRW_L02A_COMMIT")
            ?? throw new InvalidOperationException("ISRW_L02A_COMMIT is required for a persisted process proof.");
        var report = new
        {
            schemaVersion = 1,
            status = "PASS",
            requirement = new[] { "R02-01", "R02-02" },
            test = new[] { "T02-01", "T02-02" },
            commit,
            seed,
            configHash = GeometryTestSupport.ConfigHash,
            algorithmVersion = GenerationIdentity.SupportedAlgorithmVersion,
            snapshotSchemaVersion = GenerationIdentity.SupportedSchemaVersion,
            geometryProfile = identity.DeterminismProfileId,
            order = "reverse",
            workers = 16,
            cache = "precomputed",
            processId = Environment.ProcessId,
            framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            siteCount = mesh.Sites.Count,
            edgeCount = mesh.Edges.Count,
            triangleCount = mesh.Triangles.Count,
            cellCount = mesh.Cells.Count,
            geometryHash,
            boundaryFieldHash,
        };
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string BoundaryFingerprint()
    {
        var field = new SmoothField2D(11, 5, -7, 1, 3, 2, 1024);
        WorldBounds southwest = new(0, 0, 8, 8);
        WorldBounds southeast = new(8, 0, 16, 8);
        WorldBounds northwest = new(0, 8, 8, 16);
        WorldBounds northeast = new(8, 8, 16, 16);
        var canonical = new StringBuilder("L02A-BOUNDARY-PROOF-V1\n");
        for (long coordinate = 0; coordinate <= 8; coordinate++)
        {
            AppendEqualPair(
                canonical,
                field.SampleBoundary(southwest, BoundarySide.Right, coordinate),
                field.SampleBoundary(southeast, BoundarySide.Left, coordinate));
            AppendEqualPair(
                canonical,
                field.SampleBoundary(southwest, BoundarySide.Top, coordinate),
                field.SampleBoundary(northwest, BoundarySide.Bottom, coordinate));
        }
        for (long coordinate = 8; coordinate <= 16; coordinate++)
        {
            AppendEqualPair(
                canonical,
                field.SampleBoundary(northwest, BoundarySide.Right, coordinate),
                field.SampleBoundary(northeast, BoundarySide.Left, coordinate));
            AppendEqualPair(
                canonical,
                field.SampleBoundary(southeast, BoundarySide.Top, coordinate),
                field.SampleBoundary(northeast, BoundarySide.Bottom, coordinate));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static void AppendEqualPair(
        StringBuilder canonical,
        BoundaryFieldSample first,
        BoundaryFieldSample second)
    {
        Assert.AreEqual(first, second);
        canonical.Append(first.Position.X).Append('|').Append(first.Position.Z).Append('|')
            .Append(first.CanonicalValue).Append('|').Append(first.ValueBits).Append('\n');
    }
}
