using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Tests.L03B;

internal sealed record L03BBlindArtifact(string Path, string Sha256);

internal static class L03BEvidenceProtocol
{
    private const int MaximumGitBlobBytes = 1024 * 1024;

    internal const int ManifestSchemaVersion = 1;
    internal const string BlindSignatureScheme = "sha256-canonical-json-v1";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static string GitBlobObjectId(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        byte[] header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(header);
        hash.AppendData(content);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static byte[] ReadVerifiedGitBlob(string repository, string objectId)
    {
        if (objectId.Length != 40 || objectId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Evidence requires a 40-character Git object ID.", nameof(objectId));
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add($"safe.directory={repository.Replace('\\', '/')}");
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(repository);
        process.StartInfo.ArgumentList.Add("cat-file");
        process.StartInfo.ArgumentList.Add("blob");
        process.StartInfo.ArgumentList.Add(objectId);
        process.Start();

        Task<byte[]> stdout = ReadBoundedBytesAsync(process.StandardOutput.BaseStream, MaximumGitBlobBytes);
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
        try
        {
            completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        catch (TimeoutException exception)
        {
            TerminateAndDrain(process, stdout, stderr);
            throw new TimeoutException("Git blob read timed out.", exception);
        }
        catch (Exception exception)
        {
            TerminateAndDrain(process, stdout, stderr);
            throw new InvalidOperationException("Git blob read failed while collecting process output.", exception);
        }

        if (process.ExitCode != 0)
        {
            string diagnostic = stderr.Result.Trim();
            if (diagnostic.Length > 2048) diagnostic = diagnostic[..2048] + "...<truncated>";
            throw new InvalidOperationException($"Git blob read failed: {diagnostic}");
        }

        return RequireExactGitBlobBytes(objectId, stdout.Result);
    }

    internal static byte[] RequireExactGitBlobBytes(string expectedObjectId, byte[] content)
    {
        string actualObjectId = GitBlobObjectId(content);
        if (!string.Equals(expectedObjectId, actualObjectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Git blob bytes have object ID {actualObjectId}, expected {expectedObjectId}.");
        }
        return content;
    }

    internal static byte[] CreateBlindManifest(
        int schemaVersion,
        string automatedStatus,
        string qualitativeReviewStatus,
        string overallStatus,
        string commit,
        string tree,
        string fixturesBlob,
        string configuration,
        string testAssemblyHash,
        string coreAssemblyHash,
        IReadOnlyList<L03BBlindArtifact> artifacts)
    {
        L03BBlindArtifact[] ordered = artifacts.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        var payload = new BlindManifestPayload(
            schemaVersion,
            ["R03-05", "R03-06"],
            automatedStatus,
            qualitativeReviewStatus,
            overallStatus,
            commit,
            tree,
            fixturesBlob,
            configuration,
            testAssemblyHash,
            coreAssemblyHash,
            BlindSignatureScheme,
            ordered);
        byte[] canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJsonOptions);
        var manifest = new BlindManifest(
            payload.SchemaVersion,
            payload.RequirementIds,
            payload.AutomatedStatus,
            payload.QualitativeReviewStatus,
            payload.OverallStatus,
            payload.Commit,
            payload.Tree,
            payload.FixturesBlob,
            payload.Configuration,
            payload.TestAssemblySha256,
            payload.CoreAssemblySha256,
            payload.SignatureScheme,
            payload.Artifacts,
            L03BTestSupport.Sha256(canonicalPayload));
        return JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
    }

    private static void TerminateAndDrain(Process process, Task stdout, Task stderr)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }

        try
        {
            Task.WhenAll(process.WaitForExitAsync(), stdout, stderr)
                .WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The original process failure remains terminal even if an inherited pipe cannot be drained.
        }
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(Stream source, int maximumBytes)
    {
        using var content = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0) return content.ToArray();
            if (content.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"Git blob exceeds the {maximumBytes}-byte evidence limit.");
            }
            await content.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }
    }

    private sealed record BlindManifestPayload(
        int SchemaVersion,
        string[] RequirementIds,
        string AutomatedStatus,
        string QualitativeReviewStatus,
        string OverallStatus,
        string Commit,
        string Tree,
        string FixturesBlob,
        string Configuration,
        string TestAssemblySha256,
        string CoreAssemblySha256,
        string SignatureScheme,
        L03BBlindArtifact[] Artifacts);

    private sealed record BlindManifest(
        int SchemaVersion,
        string[] RequirementIds,
        string AutomatedStatus,
        string QualitativeReviewStatus,
        string OverallStatus,
        string Commit,
        string Tree,
        string FixturesBlob,
        string Configuration,
        string TestAssemblySha256,
        string CoreAssemblySha256,
        string SignatureScheme,
        L03BBlindArtifact[] Artifacts,
        string BundleSignature);
}
