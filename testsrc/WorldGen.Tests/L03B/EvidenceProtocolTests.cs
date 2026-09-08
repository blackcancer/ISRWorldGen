using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ISRWorldGen.Core.Geology.Landscapes;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class EvidenceProtocolTests
{
    private const string Commit = "d38bf70b11c08e69fc7251347668ec175f489a74";
    private const string Tree = "5577a96553ea01298ee66bba7c80984f21a169b6";
    private const string FixturesBlob = "0123456789abcdef0123456789abcdef01234567";
    private const string TestHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string CoreHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private const string RunId = "evidence-s-staging-d38bf70b11c08e69fc7251347668ec175f489a74-0123456789abcdef0123456789abcdef";
    private const string Nonce = "9999999999999999999999999999999999999999999999999999999999999999";
    private static readonly (string Code, LandscapeFamily Family)[] BlindOrder =
    [
        ("S01", LandscapeFamily.RuggedRanges),
        ("S02", LandscapeFamily.OldMassifs),
        ("S03", LandscapeFamily.Plateaus),
        ("S04", LandscapeFamily.SedimentaryBasins),
        ("S05", LandscapeFamily.Plains),
        ("S06", LandscapeFamily.VolcanicDomains),
    ];

    [TestMethod]
    public void GitBlobObjectIdUsesTheExactGitBlobFraming()
    {
        Assert.AreEqual("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", L03BEvidenceProtocol.GitBlobObjectId([]));
        Assert.AreEqual("ce013625030ba8dba906f756967f9e9ca394464a", L03BEvidenceProtocol.GitBlobObjectId(Encoding.UTF8.GetBytes("hello\n")));
    }

    [TestMethod]
    public void FixtureEvidenceConsumesCommittedBytesInsteadOfCrLfCheckoutBytes()
    {
        byte[] committedBytes = Encoding.UTF8.GetBytes("{\n  \"calibration_seeds\": [1],\n  \"holdout_seeds\": [2]\n}\n");
        byte[] checkoutBytes = Encoding.UTF8.GetBytes("{\r\n  \"calibration_seeds\": [1],\r\n  \"holdout_seeds\": [2]\r\n}\r\n");
        string committedObjectId = L03BEvidenceProtocol.GitBlobObjectId(committedBytes);

        CollectionAssert.AreEqual(committedBytes,
            L03BEvidenceProtocol.RequireExactGitBlobBytes(committedObjectId, committedBytes));
        Assert.AreNotEqual(committedObjectId, L03BEvidenceProtocol.GitBlobObjectId(checkoutBytes));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            L03BEvidenceProtocol.RequireExactGitBlobBytes(committedObjectId, checkoutBytes));

        string evidenceSource = File.ReadAllText(Path.Combine(L03BTestSupport.FindRepositoryRoot(),
            "testsrc", "WorldGen.Tests", "L03B", "EvidenceArtifactTests.cs"));
        StringAssert.Contains(evidenceSource, "ReadVerifiedGitBlob(repository, fixturesBlob)");
        Assert.IsFalse(evidenceSource.Contains("File.ReadAllBytes(fixturesPath)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BlindManifestSignatureCoversEveryPublicPayloadField()
    {
        L03BBlindArtifact[] artifacts =
        [
            new("blind/T03-06-S02.bmp", new string('b', 64)),
            new("blind/T03-06-S01.bmp", new string('a', 64)),
        ];
        string baseline = Signature(Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, Tree,
            FixturesBlob, "Release", TestHash, CoreHash, artifacts));

        foreach (byte[] changed in new[]
        {
            Manifest(2, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, Tree, FixturesBlob, "Release", TestHash, CoreHash, artifacts),
            Manifest(1, "FAIL", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, Tree, FixturesBlob, "Release", TestHash, CoreHash, artifacts),
            Manifest(1, "PASS", "NOT_RUN", "REVIEW_REQUIRED", Commit, Tree, FixturesBlob, "Release", TestHash, CoreHash, artifacts),
            Manifest(1, "PASS", "REVIEW_REQUIRED", "PASS", Commit, Tree, FixturesBlob, "Release", TestHash, CoreHash, artifacts),
            Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", new string('1', 40), Tree, FixturesBlob, "Release", TestHash, CoreHash, artifacts),
            Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, new string('2', 40), FixturesBlob, "Release", TestHash, CoreHash, artifacts),
            Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, Tree, new string('3', 40), "Release", TestHash, CoreHash, artifacts),
            Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, Tree, FixturesBlob, "Debug", TestHash, CoreHash, artifacts),
            Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, Tree, FixturesBlob, "Release", new string('4', 64), CoreHash, artifacts),
            Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, Tree, FixturesBlob, "Release", TestHash, new string('5', 64), artifacts),
            Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED", Commit, Tree, FixturesBlob, "Release", TestHash, CoreHash,
                [new("blind/T03-06-S01.bmp", new string('c', 64))]),
        })
        {
            Assert.AreNotEqual(baseline, Signature(changed));
        }

        byte[] manifestBytes = Manifest(1, "PASS", "REVIEW_REQUIRED", "REVIEW_REQUIRED",
            Commit, Tree, FixturesBlob, "Release", TestHash, CoreHash, artifacts);
        using JsonDocument manifest = JsonDocument.Parse(manifestBytes);
        string[] paths = manifest.RootElement.GetProperty("artifacts").EnumerateArray()
            .Select(item => item.GetProperty("path").GetString()!).ToArray();
        CollectionAssert.AreEqual(paths.Order(StringComparer.Ordinal).ToArray(), paths);
        string manifestText = Encoding.UTF8.GetString(manifestBytes);
        Assert.IsFalse(manifestText.Contains("nonce", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifestText.Contains("family", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifestText.Contains("seed", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifestText.Contains("site", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void BlindReviewFormRequiresIdentificationAndConfidenceBeforeKeyReveal()
    {
        byte[] formBytes = L03BEvidenceProtocol.CreateBlindReviewForm(["S03", "S01", "S02"]);
        using JsonDocument form = JsonDocument.Parse(formBytes);
        Assert.AreEqual("AWAITING_BLIND_REVIEW", form.RootElement.GetProperty("status").GetString());
        JsonElement[] entries = form.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { "S03", "S01", "S02" },
            entries.Select(entry => entry.GetProperty("code").GetString()).ToArray());
        Assert.IsTrue(entries.All(entry =>
            entry.GetProperty("identifiedFamilyBeforeReveal").ValueKind == JsonValueKind.Null &&
            entry.GetProperty("confidence0To100BeforeReveal").ValueKind == JsonValueKind.Null &&
            entry.GetProperty("morphologyObservations").ValueKind == JsonValueKind.Null));
        string instructions = string.Join(' ', form.RootElement.GetProperty("instructions")
            .EnumerateArray().Select(item => item.GetString()));
        StringAssert.Contains(instructions, "answer key remains unopened");
        StringAssert.Contains(instructions, "Hash and timestamp the completed response before requesting reveal");
        Assert.ThrowsExactly<ArgumentException>(() => L03BEvidenceProtocol.CreateBlindReviewForm(["S01", "S01"]));
        Assert.ThrowsExactly<ArgumentException>(() => L03BEvidenceProtocol.CreateBlindReviewForm(["family"]));
    }

    [TestMethod]
    public void FailureAttributionOpensOnlyTheFailingNeutralCodeAndIsBoundToItsRun()
    {
        byte[] commitments = Commitments(RunId, Nonce);
        byte[] receipt = L03BEvidenceProtocol.CreateFailureAttribution(
            commitments, "S05", LandscapeFamily.Plains, Nonce);

        Assert.AreEqual(LandscapeFamily.Plains,
            L03BEvidenceProtocol.VerifyFailureAttribution(commitments, receipt));
        string commitmentsText = Encoding.UTF8.GetString(commitments);
        string receiptText = Encoding.UTF8.GetString(receipt);
        Assert.IsFalse(commitmentsText.Contains(Nonce, StringComparison.Ordinal));
        Assert.IsFalse(Enum.GetNames<LandscapeFamily>().Any(name => commitmentsText.Contains(name, StringComparison.Ordinal)));
        Assert.IsFalse(receiptText.Contains(Nonce, StringComparison.Ordinal));
        StringAssert.Contains(receiptText, "\"code\": \"S05\"");
        StringAssert.Contains(receiptText, "\"revealedFamily\": \"Plains\"");
        Assert.IsFalse(Enum.GetNames<LandscapeFamily>()
            .Where(name => name != nameof(LandscapeFamily.Plains))
            .Any(name => receiptText.Contains(name, StringComparison.Ordinal)));

        byte[] anotherRun = Commitments(RunId + "-other", Nonce);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            L03BEvidenceProtocol.VerifyFailureAttribution(anotherRun, receipt));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            L03BEvidenceProtocol.CreateFailureAttribution(commitments, "S05", LandscapeFamily.OldMassifs, Nonce));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            L03BEvidenceProtocol.CreateFailureAttribution(commitments, "S05", LandscapeFamily.Plains, new string('a', 64)));

        JsonObject changedFamily = JsonNode.Parse(receipt)!.AsObject();
        changedFamily["revealedFamily"] = nameof(LandscapeFamily.OldMassifs);
        Assert.ThrowsExactly<InvalidDataException>(() => L03BEvidenceProtocol.VerifyFailureAttribution(
            commitments, JsonSerializer.SerializeToUtf8Bytes(changedFamily)));
        JsonObject changedOpening = JsonNode.Parse(receipt)!.AsObject();
        changedOpening["opening"] = new string('b', 64);
        Assert.ThrowsExactly<InvalidDataException>(() => L03BEvidenceProtocol.VerifyFailureAttribution(
            commitments, JsonSerializer.SerializeToUtf8Bytes(changedOpening)));
    }

    [TestMethod]
    public void RunnerTextKeepsTheExclusivePreflightAndSealedTrxProtocol()
    {
        string script = File.ReadAllText(Path.Combine(L03BTestSupport.FindRepositoryRoot(),
            "testsrc", "WorldGen.Tests", "L03B", "Run-L03BEvidenceS.ps1"));

        int lockIndex = script.IndexOf("[IO.FileStream]::new($lockPath", StringComparison.Ordinal);
        int restoreIndex = script.IndexOf("'restore', $solution, '--locked-mode'", StringComparison.Ordinal);
        Assert.IsTrue(lockIndex >= 0 && restoreIndex > lockIndex, "The exclusive lock must precede restore/build.");
        StringAssert.Contains(script, "if ($PreflightOnly) { [void](Assert-Provenance $head $tree); return }");
        StringAssert.Contains(script, "[Security.Cryptography.RandomNumberGenerator]::Fill($nonceBytes)");
        StringAssert.Contains(script, "[Threading.Tasks.Task]::WhenAny");
        StringAssert.Contains(script, "FullyQualifiedName=ISRWorldGen.Tests.L03B.EvidenceArtifactTests.T0305AndT0306PublishAtomicBlindReviewEvidence");
        StringAssert.Contains(script, "--logger', \"trx;LogFileName=$trxName\"");
        StringAssert.Contains(script, "Write-SealedManifest");
        StringAssert.Contains(script, "Publish-EvidenceFailure");
        StringAssert.Contains(script, "evidence-s-failure-$head-");
        StringAssert.Contains(script, "T03-06-S-failure-attribution.json");
        StringAssert.Contains(script, "[IO.FileOptions]::DeleteOnClose");
        Assert.IsFalse(script.Contains("[string]$Nonce", StringComparison.Ordinal));
    }

    private static byte[] Commitments(string runId, string nonce) =>
        L03BEvidenceProtocol.CreateAttributionCommitments(
            runId,
            Commit,
            Tree,
            FixturesBlob,
            "Release",
            TestHash,
            CoreHash,
            BlindOrder,
            nonce);

    private static byte[] Manifest(
        int schemaVersion,
        string automatedStatus,
        string qualitativeReviewStatus,
        string overallStatus,
        string commit,
        string tree,
        string fixturesBlob,
        string configuration,
        string testHash,
        string coreHash,
        IReadOnlyList<L03BBlindArtifact> artifacts) =>
        L03BEvidenceProtocol.CreateBlindManifest(schemaVersion, automatedStatus, qualitativeReviewStatus, overallStatus,
            commit, tree, fixturesBlob, configuration, testHash, coreHash, artifacts);

    private static string Signature(byte[] manifest)
    {
        using JsonDocument document = JsonDocument.Parse(manifest);
        return document.RootElement.GetProperty("bundleSignature").GetString()!;
    }
}
