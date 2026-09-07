using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Tests.L03B;

internal sealed record L03BBlindArtifact(string Path, string Sha256);

internal static class L03BEvidenceProtocol
{
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
