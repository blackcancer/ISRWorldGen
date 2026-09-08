using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Landscapes;

namespace ISRWorldGen.Tests.L03B;

internal sealed record L03BBlindArtifact(string Path, string Sha256);

internal sealed record L03BBlindReviewEntry(
    string Code,
    string IdentifiedFamilyBeforeReveal,
    int Confidence0To100BeforeReveal,
    string MorphologyObservations);

internal sealed record L03BVerifiedReviewReceipt(
    string ReceiptId,
    DateTimeOffset RecordedUtc,
    string RunId,
    string Commit,
    string Tree,
    string FixturesBlob,
    string BlindManifestSha256,
    string CommitmentsSha256,
    IReadOnlyList<L03BBlindReviewEntry> Entries);

internal static class L03BEvidenceProtocol
{
    private const int MaximumGitBlobBytes = 1024 * 1024;

    internal const int ManifestSchemaVersion = 1;
    internal const string BlindSignatureScheme = "sha256-canonical-json-v1";
    internal const string FailureAttributionScheme = "sha256-run-bound-selective-opening-v1";
    internal const string BlindReviewReceiptScheme = "sha256-run-bound-blind-review-receipt-v1";
    internal const string CommitmentsArtifactPath = "blind/T03-06-S-attribution-commitments.json";
    internal const string ReviewRequestArtifactPath = "blind/T03-06-S-review-request.json";
    internal const string ReviewReceiptFileName = "T03-06-S-review-receipt.json";
    internal const string FailureAttributionArtifactPath = "sealed/T03-06-S-failure-attribution.json";
    internal const string TrxArtifactPath = "sealed/T03-05-06-S.trx";
    internal const string SuccessMarkerArtifactPath = "sealed/T03-06-S-success.json";
    private static readonly string[] ExpectedNeutralCodes = ["S01", "S02", "S03", "S04", "S05", "S06"];
    private static readonly string[] FailureSourceAllowlist =
        [CommitmentsArtifactPath, FailureAttributionArtifactPath, TrxArtifactPath];

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

    internal static IReadOnlyList<string> SelectCompletedFailureArtifacts(IEnumerable<string> completedRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(completedRelativePaths);
        var completed = new HashSet<string>(completedRelativePaths, StringComparer.Ordinal);
        return FailureSourceAllowlist.Where(completed.Contains).ToArray();
    }

    internal static byte[] CreateSuccessMarker(
        string runId,
        string commit,
        string tree,
        string fixturesBlob,
        string testAssemblyHash,
        string coreAssemblyHash,
        string reportHash,
        string blindManifestHash,
        string reviewKeyHash,
        string commitmentsHash)
    {
        var marker = new SuccessMarker(
            1,
            "COMPLETE",
            runId,
            commit,
            tree,
            fixturesBlob,
            "Release",
            testAssemblyHash,
            coreAssemblyHash,
            reportHash,
            blindManifestHash,
            reviewKeyHash,
            commitmentsHash);
        ValidateSuccessMarkerFields(marker);
        return JsonSerializer.SerializeToUtf8Bytes(marker, ManifestJsonOptions);
    }

    internal static void VerifySuccessMarker(
        byte[] markerBytes,
        string runId,
        string commit,
        string tree,
        string fixturesBlob,
        string testAssemblyHash,
        string coreAssemblyHash,
        string reportHash,
        string blindManifestHash,
        string reviewKeyHash,
        string commitmentsHash)
    {
        ArgumentNullException.ThrowIfNull(markerBytes);
        SuccessMarker marker = JsonSerializer.Deserialize<SuccessMarker>(markerBytes, ManifestJsonOptions) ??
            throw new InvalidDataException("Evidence success marker is empty.");
        ValidateSuccessMarkerFields(marker);
        SuccessMarker expected = new(
            1, "COMPLETE", runId, commit, tree, fixturesBlob, "Release", testAssemblyHash, coreAssemblyHash,
            reportHash, blindManifestHash, reviewKeyHash, commitmentsHash);
        if (marker != expected)
        {
            throw new InvalidDataException("Evidence success marker does not bind the exact completed run artifacts.");
        }
    }

    internal static byte[] CreateBlindReviewRequest(IReadOnlyList<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (codes.Count == 0 || codes.Distinct(StringComparer.Ordinal).Count() != codes.Count ||
            codes.Any(code => code.Length != 3 || code[0] != 'S' || !char.IsAsciiDigit(code[1]) || !char.IsAsciiDigit(code[2])))
        {
            throw new ArgumentException("Blind review codes must be unique Sxx identifiers.", nameof(codes));
        }

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            status = "READY_FOR_EXTERNAL_BLIND_REVIEW",
            trustBoundary = "The reviewer receives only a copy of the blind directory. The campaign controller retains sealed artifacts and must not read the answer key directly.",
            instructions = new[]
            {
                "Inspect only the immutable blind package and prepare a separate JSON answers file.",
                "For every code, record exactly one identifiedFamilyBeforeReveal, an integer confidence0To100BeforeReveal from 0 to 100, and non-empty morphologyObservations.",
                "Run New-L03BBlindReviewReceipt.ps1 against the copied blind directory. It verifies every blind hash and atomically publishes a timestamped, self-hashed receipt without reading any sealed artifact.",
                "Return the complete review directory and its printed receiptFileSha256 to the controller. Only Open-L03BBlindReview.ps1 may reveal the mapping after it validates that exact expected hash.",
            },
            allowedFamilies = Enum.GetValues<LandscapeFamily>().Select(family => family.ToString()),
            codes,
        }, ManifestJsonOptions);
    }

    internal static byte[] CreateBlindReviewReceipt(
        byte[] blindManifestBytes,
        byte[] commitmentsBytes,
        IReadOnlyList<L03BBlindReviewEntry> entries,
        DateTimeOffset recordedUtc)
    {
        BlindManifest manifest = ParseAndValidateBlindManifest(blindManifestBytes, requireReviewablePass: true);
        AttributionCommitments commitments = ParseAndValidateCommitments(commitmentsBytes);
        ValidateReviewBinding(manifest, commitments, blindManifestBytes, commitmentsBytes);
        L03BBlindReviewEntry[] orderedEntries = ValidateReviewEntries(entries);
        string canonicalUtc = recordedUtc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var payload = new BlindReviewReceiptPayload(
            1,
            ["R03-06"],
            "RECORDED_BEFORE_REVEAL",
            BlindReviewReceiptScheme,
            canonicalUtc,
            commitments.Binding,
            new BoundDocument("blind/T03-06-S-manifest.json", L03BTestSupport.Sha256(blindManifestBytes), manifest.BundleSignature),
            new BoundDocument(CommitmentsArtifactPath, L03BTestSupport.Sha256(commitmentsBytes), commitments.BundleSignature),
            orderedEntries);
        byte[] canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJsonOptions);
        var receipt = new BlindReviewReceipt(
            payload.SchemaVersion,
            payload.RequirementIds,
            payload.Status,
            payload.Protocol,
            payload.RecordedUtc,
            payload.Binding,
            payload.BlindManifest,
            payload.AttributionCommitments,
            payload.Entries,
            L03BTestSupport.Sha256(canonicalPayload));
        return JsonSerializer.SerializeToUtf8Bytes(receipt, ManifestJsonOptions);
    }

    internal static L03BVerifiedReviewReceipt VerifyBlindReviewReceipt(
        byte[] blindManifestBytes,
        byte[] commitmentsBytes,
        byte[] receiptBytes)
    {
        ArgumentNullException.ThrowIfNull(receiptBytes);
        BlindManifest manifest = ParseAndValidateBlindManifest(blindManifestBytes, requireReviewablePass: true);
        AttributionCommitments commitments = ParseAndValidateCommitments(commitmentsBytes);
        ValidateReviewBinding(manifest, commitments, blindManifestBytes, commitmentsBytes);
        AssertClosedReviewReceiptSchema(receiptBytes);
        BlindReviewReceipt receipt = JsonSerializer.Deserialize<BlindReviewReceipt>(receiptBytes, ManifestJsonOptions) ??
            throw new InvalidDataException("Blind review receipt is empty.");
        if (receipt.RequirementIds is null || receipt.Binding is null || receipt.BlindManifest is null ||
            receipt.AttributionCommitments is null || receipt.Entries is null || receipt.SchemaVersion != 1 ||
            !receipt.RequirementIds.SequenceEqual(["R03-06"], StringComparer.Ordinal) ||
            receipt.Status != "RECORDED_BEFORE_REVEAL" || receipt.Protocol != BlindReviewReceiptScheme ||
            receipt.Binding != commitments.Binding ||
            receipt.BlindManifest != new BoundDocument("blind/T03-06-S-manifest.json", L03BTestSupport.Sha256(blindManifestBytes), manifest.BundleSignature) ||
            receipt.AttributionCommitments != new BoundDocument(CommitmentsArtifactPath, L03BTestSupport.Sha256(commitmentsBytes), commitments.BundleSignature) ||
            !DateTimeOffset.TryParseExact(receipt.RecordedUtc, "O", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset recordedUtc) ||
            recordedUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("Blind review receipt is malformed or belongs to another evidence run.");
        }

        L03BBlindReviewEntry[] orderedEntries;
        try
        {
            orderedEntries = ValidateReviewEntries(receipt.Entries);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Blind review receipt entries are invalid.", exception);
        }
        var payload = new BlindReviewReceiptPayload(
            receipt.SchemaVersion,
            receipt.RequirementIds,
            receipt.Status,
            receipt.Protocol,
            receipt.RecordedUtc,
            receipt.Binding,
            receipt.BlindManifest,
            receipt.AttributionCommitments,
            orderedEntries);
        string expectedReceiptId = L03BTestSupport.Sha256(JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJsonOptions));
        if (!FixedTimeEquals(receipt.ReceiptId, expectedReceiptId))
        {
            throw new InvalidDataException("Blind review receipt was modified after it was recorded.");
        }

        return new L03BVerifiedReviewReceipt(
            receipt.ReceiptId,
            recordedUtc,
            receipt.Binding.RunId,
            receipt.Binding.Commit,
            receipt.Binding.Tree,
            receipt.Binding.FixturesBlob,
            receipt.BlindManifest.Sha256,
            receipt.AttributionCommitments.Sha256,
            orderedEntries);
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

    private static BlindManifest ParseAndValidateBlindManifest(byte[] manifestBytes, bool requireReviewablePass)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        BlindManifest manifest = JsonSerializer.Deserialize<BlindManifest>(manifestBytes, ManifestJsonOptions) ??
            throw new InvalidDataException("Blind manifest is empty.");
        if (manifest.RequirementIds is null || manifest.Artifacts is null || manifest.SchemaVersion != ManifestSchemaVersion ||
            !manifest.RequirementIds.SequenceEqual(["R03-05", "R03-06"], StringComparer.Ordinal) ||
            manifest.SignatureScheme != BlindSignatureScheme || !IsLowerHex(manifest.Commit, 40) ||
            !IsLowerHex(manifest.Tree, 40) || !IsLowerHex(manifest.FixturesBlob, 40) ||
            manifest.Configuration != "Release" || !IsLowerHex(manifest.TestAssemblySha256, 64) ||
            !IsLowerHex(manifest.CoreAssemblySha256, 64) || manifest.Artifacts.Length == 0 ||
            manifest.Artifacts.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Artifacts.Length ||
            !manifest.Artifacts.SequenceEqual(manifest.Artifacts.OrderBy(item => item.Path, StringComparer.Ordinal)) ||
            manifest.Artifacts.Any(item => !IsCanonicalBlindArtifact(item)) ||
            (requireReviewablePass && (manifest.AutomatedStatus != "PASS" ||
                manifest.QualitativeReviewStatus != "REVIEW_REQUIRED" || manifest.OverallStatus != "REVIEW_REQUIRED")))
        {
            throw new InvalidDataException("Blind manifest is malformed or is not a reviewable PASS campaign.");
        }

        var payload = new BlindManifestPayload(
            manifest.SchemaVersion,
            manifest.RequirementIds,
            manifest.AutomatedStatus,
            manifest.QualitativeReviewStatus,
            manifest.OverallStatus,
            manifest.Commit,
            manifest.Tree,
            manifest.FixturesBlob,
            manifest.Configuration,
            manifest.TestAssemblySha256,
            manifest.CoreAssemblySha256,
            manifest.SignatureScheme,
            manifest.Artifacts);
        string expectedSignature = L03BTestSupport.Sha256(JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJsonOptions));
        if (!FixedTimeEquals(manifest.BundleSignature, expectedSignature))
        {
            throw new InvalidDataException("Blind manifest signature is invalid.");
        }
        return manifest;
    }

    private static void ValidateReviewBinding(
        BlindManifest manifest,
        AttributionCommitments commitments,
        byte[] manifestBytes,
        byte[] commitmentsBytes)
    {
        AttributionBinding manifestBinding = new(
            commitments.Binding.RunId,
            manifest.Commit,
            manifest.Tree,
            manifest.FixturesBlob,
            manifest.Configuration,
            manifest.TestAssemblySha256,
            manifest.CoreAssemblySha256);
        L03BBlindArtifact? commitmentArtifact = manifest.Artifacts.SingleOrDefault(
            item => item.Path == CommitmentsArtifactPath);
        if (manifestBinding != commitments.Binding || commitmentArtifact is null ||
            !FixedTimeEquals(commitmentArtifact.Sha256, L03BTestSupport.Sha256(commitmentsBytes)) ||
            !IsLowerHex(L03BTestSupport.Sha256(manifestBytes), 64))
        {
            throw new InvalidDataException("Blind manifest and attribution commitments do not bind the same evidence run.");
        }
    }

    private static L03BBlindReviewEntry[] ValidateReviewEntries(IReadOnlyList<L03BBlindReviewEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        L03BBlindReviewEntry[] ordered = entries.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        if (ordered.Length != ExpectedNeutralCodes.Length ||
            !ordered.Select(item => item.Code).SequenceEqual(ExpectedNeutralCodes, StringComparer.Ordinal) ||
            ordered.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != ordered.Length ||
            ordered.Any(item =>
                !Enum.TryParse(item.IdentifiedFamilyBeforeReveal, ignoreCase: false, out LandscapeFamily identified) ||
                !Enum.IsDefined(identified) || item.Confidence0To100BeforeReveal is < 0 or > 100 ||
                string.IsNullOrWhiteSpace(item.MorphologyObservations) ||
                item.MorphologyObservations.Length > 4096 ||
                item.MorphologyObservations != item.MorphologyObservations.Trim()))
        {
            throw new ArgumentException("Blind review requires exactly six valid, complete prereveal entries.", nameof(entries));
        }
        return ordered;
    }

    private static bool IsCanonicalBlindArtifact(L03BBlindArtifact artifact) =>
        artifact.Path.StartsWith("blind/", StringComparison.Ordinal) &&
        !artifact.Path.Contains('\\') && !artifact.Path.Contains("..", StringComparison.Ordinal) &&
        !Path.IsPathRooted(artifact.Path) && IsLowerHex(artifact.Sha256, 64);

    private static void AssertClosedReviewReceiptSchema(byte[] receiptBytes)
    {
        using JsonDocument document = JsonDocument.Parse(receiptBytes);
        JsonElement root = document.RootElement;
        AssertExactProperties(root,
            "schemaVersion", "requirementIds", "status", "protocol", "recordedUtc", "binding",
            "blindManifest", "attributionCommitments", "entries", "receiptId");
        AssertExactProperties(root.GetProperty("binding"),
            "runId", "commit", "tree", "fixturesBlob", "configuration", "testAssemblySha256", "coreAssemblySha256");
        AssertExactProperties(root.GetProperty("blindManifest"), "path", "sha256", "bundleSignature");
        AssertExactProperties(root.GetProperty("attributionCommitments"), "path", "sha256", "bundleSignature");
        foreach (JsonElement entry in root.GetProperty("entries").EnumerateArray())
        {
            AssertExactProperties(entry,
                "code", "identifiedFamilyBeforeReveal", "confidence0To100BeforeReveal", "morphologyObservations");
        }
    }

    private static void AssertExactProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("Evidence JSON contains missing or unexpected fields.");
        }
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

    private static void ValidateSuccessMarkerFields(SuccessMarker marker)
    {
        var binding = new AttributionBinding(
            marker.RunId,
            marker.Commit,
            marker.Tree,
            marker.FixturesBlob,
            marker.Configuration,
            marker.TestAssemblySha256,
            marker.CoreAssemblySha256);
        if (marker.SchemaVersion != 1 || marker.Status != "COMPLETE" || !IsValidBinding(binding) ||
            !IsLowerHex(marker.ReportSha256, 64) || !IsLowerHex(marker.BlindManifestSha256, 64) ||
            !IsLowerHex(marker.ReviewKeySha256, 64) || !IsLowerHex(marker.CommitmentsSha256, 64))
        {
            throw new InvalidDataException("Evidence success marker is malformed.");
        }
    }

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

    private sealed record BoundDocument(string Path, string Sha256, string BundleSignature);

    private sealed record BlindReviewReceiptPayload(
        int SchemaVersion,
        string[] RequirementIds,
        string Status,
        string Protocol,
        string RecordedUtc,
        AttributionBinding Binding,
        BoundDocument BlindManifest,
        BoundDocument AttributionCommitments,
        L03BBlindReviewEntry[] Entries);

    private sealed record BlindReviewReceipt(
        int SchemaVersion,
        string[] RequirementIds,
        string Status,
        string Protocol,
        string RecordedUtc,
        AttributionBinding Binding,
        BoundDocument BlindManifest,
        BoundDocument AttributionCommitments,
        L03BBlindReviewEntry[] Entries,
        string ReceiptId);

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

    private sealed record SuccessMarker(
        int SchemaVersion,
        string Status,
        string RunId,
        string Commit,
        string Tree,
        string FixturesBlob,
        string Configuration,
        string TestAssemblySha256,
        string CoreAssemblySha256,
        string ReportSha256,
        string BlindManifestSha256,
        string ReviewKeySha256,
        string CommitmentsSha256);
}
