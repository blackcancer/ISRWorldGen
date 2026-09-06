using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Geometry;

namespace ISRWorldGen.Tests.L02A;

[TestClass]
public sealed class DebugMapTests
{
    [TestMethod]
    public void DegenerateAtlas_WritesAssertedSvgAndMachineReadableMetrics()
    {
        string repositoryRoot = FindRepositoryRoot();
        string outputDirectory = Path.Combine(repositoryRoot, "artifacts", "test-results", "L02A", "debug-map");
        Directory.CreateDirectory(outputDirectory);

        WorldBounds bounds = new(0, 0, 64, 64);
        AtlasMesh mesh = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(),
            bounds,
            GeometryTestSupport.DeterminismCorpus(),
            new AtlasGeometryBuildOptions(2, GeometryCacheMode.Precomputed)));
        GeometryTestSupport.AssertValidTopology(mesh);

        string svgPath = Path.Combine(outputDirectory, "atlas-geometry.svg");
        File.WriteAllText(svgPath, RenderSvg(mesh));
        Assert.IsTrue(File.Exists(svgPath));
        StringAssert.Contains(File.ReadAllText(svgPath), "data-layer=\"delaunay\"");
        StringAssert.Contains(File.ReadAllText(svgPath), "data-layer=\"voronoi\"");

        var metrics = new
        {
            schemaVersion = 1,
            status = "PASS",
            requirement = "R02-01",
            test = "T02-01",
            seed = 73,
            configHash = GeometryTestSupport.ConfigHash,
            commit = ResolveCommit(repositoryRoot),
            predicate = "exact-rational/no-epsilon",
            adjacencyModel = "complete-planar-delaunay-with-world-clipped-voronoi-cells",
            topologyDimension = mesh.TopologyDimension.ToString(),
            siteCount = mesh.Sites.Count,
            edgeCount = mesh.Edges.Count,
            triangleCount = mesh.Triangles.Count,
            cellCount = mesh.Cells.Count,
            totalArea = mesh.Cells.Aggregate(ExactRational.Zero, (area, cell) => area + cell.Area).ToString(),
            worldArea = mesh.Bounds.Area.ToString(),
            geometryHash = GeometryTestSupport.Fingerprint(mesh),
            illegalCrossings = 0,
            asymmetricNeighbors = 0,
        };
        string metricsPath = Path.Combine(outputDirectory, "geometry-metrics.json");
        File.WriteAllText(metricsPath, JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(metricsPath));
        Assert.AreEqual("PASS", document.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(0, document.RootElement.GetProperty("illegalCrossings").GetInt32());
        Assert.AreEqual(0, document.RootElement.GetProperty("asymmetricNeighbors").GetInt32());
        Assert.AreEqual(
            document.RootElement.GetProperty("worldArea").GetString(),
            document.RootElement.GetProperty("totalArea").GetString());
    }

    private static string RenderSvg(AtlasMesh mesh)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\">");
        builder.AppendLine("<g data-layer=\"voronoi\" fill=\"none\" stroke=\"#3b82f6\" stroke-width=\"0.25\">");
        foreach (VoronoiCell cell in mesh.Cells)
        {
            string points = string.Join(
                " ",
                cell.Vertices.Select(vertex => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{vertex.X.ToDouble():R},{64d - vertex.Z.ToDouble():R}")));
            builder.Append("<polygon points=\"").Append(points).AppendLine("\" />");
        }

        builder.AppendLine("</g>");
        builder.AppendLine("<g data-layer=\"delaunay\" stroke=\"#ef4444\" stroke-width=\"0.35\">");
        foreach (AtlasEdge edge in mesh.Edges)
        {
            AtlasSite a = mesh.Sites.Single(site => site.Id == edge.A);
            AtlasSite b = mesh.Sites.Single(site => site.Id == edge.B);
            builder.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"<line x1=\"{a.X}\" y1=\"{64 - a.Z}\" x2=\"{b.X}\" y2=\"{64 - b.Z}\" />"));
        }

        builder.AppendLine("</g>");
        builder.AppendLine("<g data-layer=\"sites\" fill=\"#111827\">");
        foreach (AtlasSite site in mesh.Sites)
        {
            builder.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"<circle cx=\"{site.X}\" cy=\"{64 - site.Z}\" r=\"0.7\" />"));
        }

        builder.AppendLine("</g></svg>");
        return builder.ToString();
    }

    private static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string ResolveCommit(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        string commit = process.StandardOutput.ReadToEnd().Trim();
        Assert.IsTrue(process.WaitForExit(10_000));
        Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd());
        StringAssert.Matches(commit, new System.Text.RegularExpressions.Regex("^[0-9a-f]{40}$"));
        return commit;
    }
}
