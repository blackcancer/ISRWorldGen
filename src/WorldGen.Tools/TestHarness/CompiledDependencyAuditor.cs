using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ISRWorldGen.Tools.TestHarness;

internal sealed record DependencyAuditReport(
    int RunnerSchemaVersion,
    string Status,
    int ExitCode,
    string Commit,
    string InspectionMode,
    string[] Roots,
    string[] DirectReferences,
    string[] TransitiveReferences,
    string[] ForbiddenReferences,
    string[] NativeImports,
    string[] PackageDependencies);

internal static class CompiledDependencyAuditor
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Vintagestory",
        "OpenTK",
        "Silk.NET",
        "SDL",
        "wgpu",
        "System.Windows.Forms",
        "PresentationFramework",
        "WindowsBase",
    ];

    internal static DependencyAuditReport Audit(string rootAssemblyPath, string commit)
    {
        string fullRoot = Path.GetFullPath(rootAssemblyPath);
        string assemblyDirectory = Path.GetDirectoryName(fullRoot)
            ?? throw new ArgumentException("The assembly path has no directory.", nameof(rootAssemblyPath));
        var pending = new Queue<string>();
        var inspected = new Dictionary<string, AssemblyInspection>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(fullRoot);

        while (pending.TryDequeue(out string? assemblyPath))
        {
            AssemblyInspection inspection = InspectAssembly(assemblyPath);
            if (!inspected.TryAdd(inspection.Name, inspection))
            {
                continue;
            }

            foreach (string reference in inspection.References.Where(IsProjectAssembly))
            {
                string candidate = Path.Combine(assemblyDirectory, reference + ".dll");
                if (File.Exists(candidate) && !inspected.ContainsKey(reference))
                {
                    pending.Enqueue(candidate);
                }
            }
        }

        string rootName = InspectAssembly(fullRoot).Name;
        string[] direct = inspected[rootName].References.Order(StringComparer.Ordinal).ToArray();
        string[] transitive = inspected.Values
            .Where(value => !string.Equals(value.Name, rootName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(value => value.References)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] allReferences = inspected.Values
            .SelectMany(value => value.References)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] forbidden = allReferences
            .Where(reference => ForbiddenPrefixes.Any(prefix => reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] nativeImports = inspected.Values
            .SelectMany(value => value.NativeImports.Select(method => $"{value.Name}:{method}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] packages = InspectPackageDependencies(Path.ChangeExtension(fullRoot, ".deps.json"));
        bool passed = forbidden.Length == 0 && nativeImports.Length == 0 && packages.Length == 0;

        return new DependencyAuditReport(
            1,
            passed ? "PASS" : "FAIL",
            passed ? 0 : 1,
            commit,
            "compiled-metadata-and-deps-json",
            inspected.Keys.Order(StringComparer.Ordinal).ToArray(),
            direct,
            transitive,
            forbidden,
            nativeImports,
            packages);
    }

    private static AssemblyInspection InspectAssembly(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
        {
            throw new BadImageFormatException("The compiled file has no managed metadata.", assemblyPath);
        }

        MetadataReader reader = peReader.GetMetadataReader();
        string name = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(assemblyPath);
        string[] references = reader.AssemblyReferences
            .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] nativeImports = reader.MethodDefinitions
            .Select(handle => reader.GetMethodDefinition(handle))
            .Where(definition => (definition.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(definition => reader.GetString(definition.Name))
            .ToArray();
        return new AssemblyInspection(name, references, nativeImports);
    }

    private static string[] InspectPackageDependencies(string depsJsonPath)
    {
        if (!File.Exists(depsJsonPath))
        {
            return ["<missing-deps-json>"];
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(depsJsonPath));
        if (!document.RootElement.TryGetProperty("libraries", out JsonElement libraries))
        {
            return ["<missing-libraries-section>"];
        }

        return libraries.EnumerateObject()
            .Where(library => library.Value.TryGetProperty("type", out JsonElement type) && type.GetString() == "package")
            .Select(library => library.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsProjectAssembly(string reference) =>
        reference.StartsWith("ISRWorldGen.", StringComparison.Ordinal);

    private sealed record AssemblyInspection(string Name, string[] References, string[] NativeImports);
}
