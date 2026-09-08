using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Landscapes;

namespace ISRWorldGen.Tests.L03B;

internal sealed record L03BBlindArtifact(string Path, string Sha256);

internal static class L03BEvidenceProtocol
{
    private const int MaximumGitBlobBytes = 1024 * 1024;

    internal const int ManifestSchemaVersion = 1;
    internal const string BlindSignatureScheme = "sha256-canonical-json-v1";
    internal const string FailureAttributionScheme = "sha256-run-bound-selective-opening-v1";
    private static readonly string[] ExpectedNeutralCodes = ["S01", "S02", "S03", "S04", "S05", "S06"];

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

    internal static byte[] CreateAttributionCommitments(
        string runId,
        string commit,
        string tree,
        string fixturesBlob,
        string configuration,
        string testAssemblyHash,
        string coreAssemblyHash,
        IReadOnlyList<(string Code, LandscapeFamily Family)> blindOrder,
        string masterNonce)
    {
        AttributionBinding binding = ValidateAttributionInputs(
            runId, commit, tree, fixturesBlob, configuration, testAssemblyHash, coreAssemblyHash, blindOrder, masterNonce);
        AttributionCommitmentEntry[] entries = blindOrder
            .Select(item => new AttributionCommitmentEntry(
                item.Code,
                Commitment(binding, item.Code, item.Family, DeriveOpening(binding, item.Code, masterNonce))))
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();
        var payload = new AttributionCommitmentPayload(
            1,
            ["R03-06"],
            FailureAttributionScheme,
            binding,
            entries);
        byte[] canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJsonOptions);
        var document = new AttributionCommitments(
            payload.SchemaVersion,
            payload.RequirementIds,
            payload.Scheme,
            payload.Binding,
            payload.Entries,
            L03BTestSupport.Sha256(canonicalPayload));
        return JsonSerializer.SerializeToUtf8Bytes(document, ManifestJsonOptions);
    }

    internal static byte[] CreateFailureAttribution(
        byte[] commitmentsBytes,
        string code,
        LandscapeFamily family,
        string masterNonce)
    {
        AttributionCommitments commitments = ParseAndValidateCommitments(commitmentsBytes);
        ValidateNonce(masterNonce);
        AttributionCommitmentEntry entry = commitments.Entries.SingleOrDefault(item => item.Code == code) ??
            throw new InvalidOperationException($"No attribution commitment exists for neutral code {code}.");
        string opening = DeriveOpening(commitments.Binding, code, masterNonce);
        string expected = Commitment(commitments.Binding, code, family, opening);
        if (!FixedTimeEquals(entry.CommitmentSha256, expected))
        {
            throw new InvalidOperationException("Selective failure attribution does not open the committed neutral code.");
        }

        var receipt = new FailureAttribution(
            1,
            ["R03-06"],
            "FAIL",
            FailureAttributionScheme,
            commitments.Binding,
            L03BTestSupport.Sha256(commitmentsBytes),
            code,
            family.ToString(),
            opening,
            "This receipt opens only the failing neutral code. Verify it against the exact committed bundle; do not substitute a review key from another run.");
        byte[] receiptBytes = JsonSerializer.SerializeToUtf8Bytes(receipt, ManifestJsonOptions);
        if (VerifyFailureAttribution(commitmentsBytes, receiptBytes) != family)
        {
            throw new InvalidOperationException("Selective failure attribution failed its self-verification.");
        }
        return receiptBytes;
    }

    internal static LandscapeFamily VerifyFailureAttribution(byte[] commitmentsBytes, byte[] receiptBytes)
    {
        ArgumentNullException.ThrowIfNull(commitmentsBytes);
        ArgumentNullException.ThrowIfNull(receiptBytes);
        AttributionCommitments commitments = ParseAndValidateCommitments(commitmentsBytes);
        FailureAttribution receipt = JsonSerializer.Deserialize<FailureAttribution>(receiptBytes, ManifestJsonOptions) ??
            throw new InvalidDataException("Failure attribution receipt is empty.");
        if (receipt.RequirementIds is null || receipt.Binding is null || receipt.Code is null ||
            receipt.RevealedFamily is null || receipt.SchemaVersion != 1 ||
            !receipt.RequirementIds.SequenceEqual(["R03-06"], StringComparer.Ordinal) ||
            receipt.Status != "FAIL" || receipt.Scheme != FailureAttributionScheme ||
            receipt.Binding != commitments.Binding ||
            !FixedTimeEquals(receipt.CommitmentBundleSha256, L03BTestSupport.Sha256(commitmentsBytes)) ||
            !ExpectedNeutralCodes.Contains(receipt.Code, StringComparer.Ordinal) ||
            !Enum.TryParse(receipt.RevealedFamily, ignoreCase: false, out LandscapeFamily family) || !Enum.IsDefined(family) ||
            !IsLowerHex(receipt.Opening, 64))
        {
            throw new InvalidDataException("Failure attribution receipt is malformed or belongs to another evidence run.");
        }

        AttributionCommitmentEntry entry = commitments.Entries.SingleOrDefault(item => item.Code == receipt.Code) ??
            throw new InvalidDataException("Failure attribution code is absent from the committed bundle.");
        string expected = Commitment(commitments.Binding, receipt.Code, family, receipt.Opening);
        if (!FixedTimeEquals(entry.CommitmentSha256, expected))
        {
            throw new InvalidDataException("Failure attribution receipt does not open its committed neutral code.");
        }
        return family;
    }

    internal static byte[] CreateBlindReviewForm(IReadOnlyList<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (codes.Count == 0 || codes.Distinct(StringComparer.Ordinal).Count() != codes.Count ||
            codes.Any(code => code.Length != 3 || code[0] != 'S' || !char.IsAsciiDigit(code[1]) || !char.IsAsciiDigit(code[2])))
        {
            throw new ArgumentException("Blind review codes must be unique Sxx identifiers.", nameof(codes));
        }

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            status = "AWAITING_BLIND_REVIEW",
            instructions = new[]
            {
                "Copy this template outside the immutable evidence bundle.",
                "For every code, record exactly one identifiedFamilyBeforeReveal, confidence from 0 to 100, and morphology observations while the sealed answer key remains unopened.",
                "Hash and timestamp the completed response before requesting reveal of sealed/T03-06-S-review-key.json.",
                "After reveal, append identifiedCorrectly for every entry; do not rewrite the prereveal identification or confidence.",
            },
            allowedFamilies = Enum.GetValues<LandscapeFamily>().Select(family => family.ToString()),
            entries = codes.Select(code => new
            {
                code,
                identifiedFamilyBeforeReveal = (string?)null,
                confidence0To100BeforeReveal = (int?)null,
                morphologyObservations = (string?)null,
                identifiedCorrectlyAfterReveal = (bool?)null,
            }),
        }, ManifestJsonOptions);
    }

    private static AttributionBinding ValidateAttributionInputs(
        string runId,
        string commit,
        string tree,
        string fixturesBlob,
        string configuration,
        string testAssemblyHash,
        string coreAssemblyHash,
        IReadOnlyList<(string Code, LandscapeFamily Family)> blindOrder,
        string masterNonce)
    {
        ArgumentNullException.ThrowIfNull(blindOrder);
        var binding = new AttributionBinding(runId, commit, tree, fixturesBlob, configuration, testAssemblyHash, coreAssemblyHash);
        if (!IsValidBinding(binding) || blindOrder.Count != Enum.GetValues<LandscapeFamily>().Length ||
            blindOrder.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != blindOrder.Count ||
            !blindOrder.Select(item => item.Code).Order(StringComparer.Ordinal).SequenceEqual(ExpectedNeutralCodes, StringComparer.Ordinal) ||
            blindOrder.Select(item => item.Family).Distinct().Count() != blindOrder.Count ||
            blindOrder.Any(item => !Enum.IsDefined(item.Family)))
        {
            throw new ArgumentException("Failure attribution requires one valid neutral code per family and exact run provenance.");
        }
        ValidateNonce(masterNonce);
        return binding;
    }

    private static AttributionCommitments ParseAndValidateCommitments(byte[] commitmentsBytes)
    {
        ArgumentNullException.ThrowIfNull(commitmentsBytes);
        AttributionCommitments commitments = JsonSerializer.Deserialize<AttributionCommitments>(commitmentsBytes, ManifestJsonOptions) ??
            throw new InvalidDataException("Attribution commitment bundle is empty.");
        if (commitments.RequirementIds is null || commitments.Binding is null || commitments.Entries is null ||
            commitments.SchemaVersion != 1 || !commitments.RequirementIds.SequenceEqual(["R03-06"], StringComparer.Ordinal) ||
            commitments.Scheme != FailureAttributionScheme || !IsValidBinding(commitments.Binding) ||
            commitments.Entries.Length != Enum.GetValues<LandscapeFamily>().Length ||
            commitments.Entries.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != commitments.Entries.Length ||
            !commitments.Entries.Select(item => item.Code).SequenceEqual(ExpectedNeutralCodes, StringComparer.Ordinal) ||
            commitments.Entries.Any(item => !IsLowerHex(item.CommitmentSha256, 64)))
        {
            throw new InvalidDataException("Attribution commitment bundle is malformed.");
        }

        var payload = new AttributionCommitmentPayload(
            commitments.SchemaVersion,
            commitments.RequirementIds,
            commitments.Scheme,
            commitments.Binding,
            commitments.Entries);
        string expectedSignature = L03BTestSupport.Sha256(JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJsonOptions));
        if (!FixedTimeEquals(commitments.BundleSignature, expectedSignature))
        {
            throw new InvalidDataException("Attribution commitment bundle signature is invalid.");
        }
        return commitments;
    }

    private static string DeriveOpening(AttributionBinding binding, string code, string masterNonce)
    {
        byte[] key = Convert.FromHexString(masterNonce);
        byte[] context = JsonSerializer.SerializeToUtf8Bytes(
            new AttributionOpeningContext("ISRW-L03B-FAILURE-OPENING-V1", binding, code), CanonicalJsonOptions);
        try
        {
            using var hmac = new HMACSHA256(key);
            return Convert.ToHexString(hmac.ComputeHash(context)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(context);
        }
    }

    private static string Commitment(AttributionBinding binding, string code, LandscapeFamily family, string opening)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new AttributionOpening("ISRW-L03B-FAILURE-COMMITMENT-V1", binding, code, family.ToString(), opening),
            CanonicalJsonOptions);
        try
        {
            return L03BTestSupport.Sha256(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static void ValidateNonce(string masterNonce)
    {
        if (!IsLowerHex(masterNonce, 64))
        {
            throw new ArgumentException("Evidence master nonce must be 32 canonical lower-case bytes.", nameof(masterNonce));
        }
    }

    private static bool IsValidBinding(AttributionBinding binding) =>
        !string.IsNullOrWhiteSpace(binding.RunId) && binding.RunId.Length <= 255 &&
        binding.RunId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !binding.RunId.Contains("..", StringComparison.Ordinal) &&
        IsLowerHex(binding.Commit, 40) && IsLowerHex(binding.Tree, 40) && IsLowerHex(binding.FixturesBlob, 40) &&
        binding.Configuration == "Release" && IsLowerHex(binding.TestAssemblySha256, 64) &&
        IsLowerHex(binding.CoreAssemblySha256, 64);

    private static bool IsLowerHex(string? value, int length) => value is not null && value.Length == length &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool FixedTimeEquals(string left, string right)
    {
        if (!IsLowerHex(left, 64) || !IsLowerHex(right, 64)) return false;
        byte[] leftBytes = Convert.FromHexString(left);
        byte[] rightBytes = Convert.FromHexString(right);
        try
        {
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
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

    private sealed record AttributionBinding(
        string RunId,
        string Commit,
        string Tree,
        string FixturesBlob,
        string Configuration,
        string TestAssemblySha256,
        string CoreAssemblySha256);

    private sealed record AttributionCommitmentEntry(string Code, string CommitmentSha256);

    private sealed record AttributionCommitmentPayload(
        int SchemaVersion,
        string[] RequirementIds,
        string Scheme,
        AttributionBinding Binding,
        AttributionCommitmentEntry[] Entries);

    private sealed record AttributionCommitments(
        int SchemaVersion,
        string[] RequirementIds,
        string Scheme,
        AttributionBinding Binding,
        AttributionCommitmentEntry[] Entries,
        string BundleSignature);

    private sealed record AttributionOpeningContext(string Domain, AttributionBinding Binding, string Code);

    private sealed record AttributionOpening(
        string Domain,
        AttributionBinding Binding,
        string Code,
        string Family,
        string Opening);

    private sealed record FailureAttribution(
        int SchemaVersion,
        string[] RequirementIds,
        string Status,
        string Scheme,
        AttributionBinding Binding,
        string CommitmentBundleSha256,
        string Code,
        string RevealedFamily,
        string Opening,
        string VerificationInstruction);
}
