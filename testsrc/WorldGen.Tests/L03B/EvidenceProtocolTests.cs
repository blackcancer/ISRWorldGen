using System.Diagnostics;
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
    public void SuccessMarkerBindsTheCompletedArtifactsWithoutContainingTheReviewSecret()
    {
        string reportHash = new('1', 64);
        string manifestHash = new('2', 64);
        string reviewKeyHash = new('3', 64);
        string commitmentsHash = new('4', 64);
        byte[] marker = L03BEvidenceProtocol.CreateSuccessMarker(
            RunId, Commit, Tree, FixturesBlob, TestHash, CoreHash,
            reportHash, manifestHash, reviewKeyHash, commitmentsHash);

        L03BEvidenceProtocol.VerifySuccessMarker(
            marker, RunId, Commit, Tree, FixturesBlob, TestHash, CoreHash,
            reportHash, manifestHash, reviewKeyHash, commitmentsHash);
        string markerText = Encoding.UTF8.GetString(marker);
        StringAssert.Contains(markerText, "\"status\": \"COMPLETE\"");
        Assert.IsFalse(markerText.Contains(Nonce, StringComparison.Ordinal));
        Assert.IsFalse(Enum.GetNames<LandscapeFamily>()
            .Any(name => markerText.Contains(name, StringComparison.Ordinal)));

        JsonObject changedKey = JsonNode.Parse(marker)!.AsObject();
        changedKey["reviewKeySha256"] = new string('5', 64);
        Assert.ThrowsExactly<InvalidDataException>(() => L03BEvidenceProtocol.VerifySuccessMarker(
            JsonSerializer.SerializeToUtf8Bytes(changedKey), RunId, Commit, Tree, FixturesBlob,
            TestHash, CoreHash, reportHash, manifestHash, reviewKeyHash, commitmentsHash));
        Assert.ThrowsExactly<InvalidDataException>(() => L03BEvidenceProtocol.VerifySuccessMarker(
            marker, RunId + "-other", Commit, Tree, FixturesBlob, TestHash, CoreHash,
            reportHash, manifestHash, reviewKeyHash, commitmentsHash));
    }

    [TestMethod]
    public void FailurePublicationAllowlistDropsPostKeyAndInterruptedPartialState()
    {
        string[] postKeyCandidateState =
        [
            L03BEvidenceProtocol.CommitmentsArtifactPath,
            L03BEvidenceProtocol.FailureAttributionArtifactPath,
            L03BEvidenceProtocol.TrxArtifactPath,
            "sealed/T03-06-S-review-key.json",
            L03BEvidenceProtocol.SuccessMarkerArtifactPath,
            "sealed/T03-05-06-S.json",
            "blind/T03-06-S-manifest.json",
            "sealed/T03-06-S-progress.json",
            "blind/T03-06-S05.bmp",
        ];
        CollectionAssert.AreEqual(
            new[]
            {
                L03BEvidenceProtocol.CommitmentsArtifactPath,
                L03BEvidenceProtocol.FailureAttributionArtifactPath,
                L03BEvidenceProtocol.TrxArtifactPath,
            },
            L03BEvidenceProtocol.SelectCompletedFailureArtifacts(postKeyCandidateState).ToArray(),
            "A failure publication must never reuse the complete key, success marker, RUNNING/PASS report, manifest, progress, or maps.");

        string[] logicalTimeoutState =
        [
            L03BEvidenceProtocol.CommitmentsArtifactPath,
            "sealed/T03-05-06-S.trx.partial",
            "sealed/T03-05-06-S.json",
            "blind/T03-06-S-manifest.json",
            "sealed/T03-06-S-review-key.json",
        ];
        CollectionAssert.AreEqual(
            new[] { L03BEvidenceProtocol.CommitmentsArtifactPath },
            L03BEvidenceProtocol.SelectCompletedFailureArtifacts(logicalTimeoutState).ToArray(),
            "A logical interruption with an incomplete TRX retains only the already-atomic neutral commitments.");
    }

    [TestMethod]
    public void ReviewKeyIsWrittenOnceAndOnlyAfterTheCompleteMarker()
    {
        string evidenceSource = File.ReadAllText(Path.Combine(L03BTestSupport.FindRepositoryRoot(),
            "testsrc", "WorldGen.Tests", "L03B", "EvidenceArtifactTests.cs"));
        int corpusComplete = evidenceSource.IndexOf("Assert.HasCount(256, corpus)", StringComparison.Ordinal);
        int keyMaterialized = evidenceSource.IndexOf("byte[] keyBytes = JsonSerializer.SerializeToUtf8Bytes", StringComparison.Ordinal);
        int markerWritten = evidenceSource.IndexOf("WriteAtomic(successMarkerPath, successMarkerBytes)", StringComparison.Ordinal);
        const string keyWrite = "WriteAtomic(keyPath, keyBytes)";
        int keyWritten = evidenceSource.IndexOf(keyWrite, StringComparison.Ordinal);

        Assert.IsTrue(corpusComplete >= 0 && keyMaterialized > corpusComplete && markerWritten > keyMaterialized && keyWritten > markerWritten,
            "Corpus assertions and COMPLETE marker must precede the only complete review-key write.");
        Assert.AreEqual(keyWritten, evidenceSource.LastIndexOf(keyWrite, StringComparison.Ordinal),
            "No early or alternate review-key write is permitted.");
        StringAssert.Contains(evidenceSource, "TryDeleteSensitiveArtifact(keyPath)");
        StringAssert.Contains(evidenceSource, "CryptographicOperations.ZeroMemory(keyBytes)");
    }

    [TestMethod]
    public void FailurePublisherReconcilesPostKeyTimeoutWithoutLeakingPartialOrSecretFiles()
    {
        string repository = L03BTestSupport.FindRepositoryRoot();
        string runnerPath = Path.Combine(repository, "testsrc", "WorldGen.Tests", "L03B", "Run-L03BEvidenceS.ps1");
        string runner = File.ReadAllText(runnerPath);
        int functionsStart = runner.IndexOf("function Protect-EvidenceDiagnostic", StringComparison.Ordinal);
        int functionsEnd = runner.IndexOf("[void][IO.Directory]::CreateDirectory($localRoot)", functionsStart, StringComparison.Ordinal);
        Assert.IsTrue(functionsStart >= 0 && functionsEnd > functionsStart);

        string temporaryRoot = Path.Combine(Path.GetTempPath(), "isr-l03b-failure-protocol-" + Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(temporaryRoot, RunId);
        string failureTerminal = Path.Combine(temporaryRoot, "failure-terminal");
        string resultPath = Path.Combine(temporaryRoot, "timeout-result.json");
        const string secretSentinel = "complete-review-key-secret-sentinel";
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            File.WriteAllBytes(Path.Combine(temporaryRoot, "commitments-source.json"), Commitments(RunId, Nonce));

            string childPath = Path.Combine(temporaryRoot, "controlled-post-key-child.ps1");
            string child = """
                $ErrorActionPreference = 'Stop'
                $base = $env:ISR_L03B_PROTOCOL_TEST_ROOT
                $runId = $env:ISR_L03B_PROTOCOL_TEST_RUN
                $secret = $env:ISR_L03B_PROTOCOL_TEST_SECRET
                $staging = Join-Path $base $runId
                $trxStaging = Join-Path $base 'trx-staging'
                [void][IO.Directory]::CreateDirectory((Join-Path $staging 'blind'))
                [void][IO.Directory]::CreateDirectory((Join-Path $staging 'sealed'))
                [void][IO.Directory]::CreateDirectory($trxStaging)
                function Write-ChildAtomicText {
                    param([string]$Path, [string]$Content)
                    $temporaryPath = "$Path.tmp"
                    [IO.File]::WriteAllText($temporaryPath, $Content)
                    [IO.File]::Move($temporaryPath, $Path, $true)
                }
                $commitmentsPath = Join-Path $staging 'blind/T03-06-S-attribution-commitments.json'
                [IO.File]::Copy((Join-Path $base 'commitments-source.json'), "$commitmentsPath.tmp", $false)
                [IO.File]::Move("$commitmentsPath.tmp", $commitmentsPath, $true)
                Write-ChildAtomicText -Path (Join-Path $staging 'sealed/T03-05-06-S.json') -Content '{"overallStatus":"RUNNING"}'
                Write-ChildAtomicText -Path (Join-Path $staging 'blind/T03-06-S-manifest.json') -Content '{"overallStatus":"RUNNING"}'
                Write-ChildAtomicText -Path (Join-Path $staging 'blind/T03-06-S05.bmp') -Content 'partial-map'
                Write-ChildAtomicText -Path (Join-Path $trxStaging 'T03-05-06-S.trx') -Content '<TestRun><ResultSummary><Counters total="1" executed="0" passed="0" failed="0" /></ResultSummary></TestRun>'
                Write-ChildAtomicText -Path (Join-Path $staging 'sealed/T03-06-S-success.json') -Content '{"status":"COMPLETE"}'
                Write-ChildAtomicText -Path (Join-Path $staging 'sealed/T03-06-S-review-key.json') -Content $secret
                Write-ChildAtomicText -Path (Join-Path $staging 'child-pid.txt') -Content ([string]$PID)
                Write-ChildAtomicText -Path (Join-Path $staging 'post-key-ready.txt') -Content 'READY'
                Start-Sleep -Seconds 30
                """;
            File.WriteAllText(childPath, child, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            string functionText = runner[functionsStart..functionsEnd];
            string harnessPath = Path.Combine(temporaryRoot, "failure-harness.ps1");
            string harness = "$ErrorActionPreference = 'Stop'\n" + functionText + "\n" + """
                function Assert-Provenance {
                    param([string]$ExpectedHead, [string]$ExpectedTree)
                    return [pscustomobject]@{ Head = $ExpectedHead; Tree = $ExpectedTree }
                }
                $base = $env:ISR_L03B_PROTOCOL_TEST_ROOT
                $root = $base
                $stagingPath = Join-Path $base $env:ISR_L03B_PROTOCOL_TEST_RUN
                $failureStagingPath = Join-Path $base 'failure-publishing'
                $failureTerminalPath = Join-Path $base 'failure-terminal'
                $trxStagingPath = Join-Path $base 'trx-staging'
                $childPath = Join-Path $base 'controlled-post-key-child.ps1'
                $lockPath = Join-Path $base 'evidence-s-runner.lock'
                $runLock = $null
                $terminalPublished = $false
                $timedOut = $false
                $postKeyReached = $false
                $childProcessId = $null
                try {
                    $runLock = [IO.FileStream]::new($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 1, [IO.FileOptions]::DeleteOnClose)
                    try {
                        try {
                            [void](Invoke-EvidenceProcess -FileName pwsh -Arguments @('-NoProfile', '-NonInteractive', '-File', $childPath) -TimeoutSeconds 3 -Secrets @($env:ISR_L03B_PROTOCOL_TEST_SECRET))
                            throw 'Controlled child unexpectedly completed before timeout.'
                        } catch {
                            if (-not $_.Exception.Message.Contains('timed out after 3 seconds')) { throw }
                            $timedOut = $true
                            $readyPath = Join-Path $stagingPath 'post-key-ready.txt'
                            $keyPath = Join-Path $stagingPath 'sealed/T03-06-S-review-key.json'
                            $pidPath = Join-Path $stagingPath 'child-pid.txt'
                            $postKeyReached = (Test-Path -LiteralPath $readyPath -PathType Leaf) -and
                                (Test-Path -LiteralPath $keyPath -PathType Leaf) -and
                                ([IO.File]::ReadAllText($keyPath) -eq $env:ISR_L03B_PROTOCOL_TEST_SECRET)
                            if (-not $postKeyReached) { throw 'Controlled child did not reach the post-key state before timeout.' }
                            $childProcessId = [int][IO.File]::ReadAllText($pidPath)
                            $terminalPublished = Publish-EvidenceFailure -StagingPath $stagingPath -FailureStagingPath $failureStagingPath -FailureTerminalPath $failureTerminalPath `
                                -TrxStagingPath $trxStagingPath -TrxName 'T03-05-06-S.trx' -Head $env:ISR_L03B_PROTOCOL_TEST_COMMIT `
                                -Tree $env:ISR_L03B_PROTOCOL_TEST_TREE -FixturesBlob $env:ISR_L03B_PROTOCOL_TEST_FIXTURES `
                                -TestAssemblyHash $env:ISR_L03B_PROTOCOL_TEST_TEST_HASH -CoreAssemblyHash $env:ISR_L03B_PROTOCOL_TEST_CORE_HASH
                            throw
                        }
                    } catch {
                        if (-not $timedOut -or -not $terminalPublished) { throw }
                    } finally {
                        if (Test-Path -LiteralPath $stagingPath) { Remove-Item -LiteralPath $stagingPath -Recurse -Force }
                        if (Test-Path -LiteralPath $failureStagingPath) { Remove-Item -LiteralPath $failureStagingPath -Recurse -Force }
                        if (Test-Path -LiteralPath $trxStagingPath) { Remove-Item -LiteralPath $trxStagingPath -Recurse -Force }
                    }
                } finally {
                    if ($null -ne $runLock) { $runLock.Dispose() }
                    if (Test-Path -LiteralPath $lockPath) { Remove-Item -LiteralPath $lockPath -Force }
                }
                $childTerminated = $null -eq (Get-Process -Id $childProcessId -ErrorAction SilentlyContinue)
                $sourceClean = -not (Test-Path -LiteralPath $stagingPath)
                $failurePublishingClean = -not (Test-Path -LiteralPath $failureStagingPath)
                $trxClean = -not (Test-Path -LiteralPath $trxStagingPath)
                $lockClean = -not (Test-Path -LiteralPath $lockPath)
                if (-not $childTerminated -or -not $sourceClean -or -not $failurePublishingClean -or -not $trxClean -or -not $lockClean) {
                    throw 'Timeout reconciliation did not terminate the child and clean every non-terminal path.'
                }
                $result = [ordered]@{
                    timedOut = $timedOut
                    postKeyReached = $postKeyReached
                    childTerminated = $childTerminated
                    sourceClean = $sourceClean
                    failurePublishingClean = $failurePublishingClean
                    trxClean = $trxClean
                    lockClean = $lockClean
                }
                [IO.File]::WriteAllText((Join-Path $base 'timeout-result.json'), ($result | ConvertTo-Json))
                """;
            File.WriteAllText(harnessPath, harness, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("pwsh")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(harnessPath);
            process.StartInfo.Environment["ISR_L03B_PROTOCOL_TEST_ROOT"] = temporaryRoot;
            process.StartInfo.Environment["ISR_L03B_PROTOCOL_TEST_RUN"] = RunId;
            process.StartInfo.Environment["ISR_L03B_PROTOCOL_TEST_SECRET"] = secretSentinel;
            process.StartInfo.Environment["ISR_L03B_PROTOCOL_TEST_COMMIT"] = Commit;
            process.StartInfo.Environment["ISR_L03B_PROTOCOL_TEST_TREE"] = Tree;
            process.StartInfo.Environment["ISR_L03B_PROTOCOL_TEST_FIXTURES"] = FixturesBlob;
            process.StartInfo.Environment["ISR_L03B_PROTOCOL_TEST_TEST_HASH"] = TestHash;
            process.StartInfo.Environment["ISR_L03B_PROTOCOL_TEST_CORE_HASH"] = CoreHash;
            process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Failure-publisher harness exceeded its bounded integration timeout.");
            }
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            Assert.AreEqual(0, process.ExitCode, $"Failure-publisher harness failed. stdout=[{stdout}] stderr=[{stderr}]");

            using JsonDocument result = JsonDocument.Parse(File.ReadAllBytes(resultPath));
            foreach (string property in new[] { "timedOut", "postKeyReached", "childTerminated", "sourceClean", "failurePublishingClean", "trxClean", "lockClean" })
            {
                Assert.IsTrue(result.RootElement.GetProperty(property).GetBoolean(), property);
            }

            string[] published = Directory.GetFiles(failureTerminal, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(failureTerminal, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "blind/T03-06-S-attribution-commitments.json",
                "blind/T03-06-S-manifest.json",
                "sealed/T03-05-06-S-manifest.json",
                "sealed/T03-05-06-S.json",
            }, published);
            string terminalText = string.Join('\n', published.Select(path => File.ReadAllText(Path.Combine(failureTerminal, path.Replace('/', Path.DirectorySeparatorChar)))));
            Assert.IsFalse(terminalText.Contains(secretSentinel, StringComparison.Ordinal));
            Assert.IsFalse(terminalText.Contains("\"overallStatus\": \"RUNNING\"", StringComparison.Ordinal));
            Assert.IsFalse(terminalText.Contains("\"overallStatus\":\"RUNNING\"", StringComparison.Ordinal));
            Assert.IsFalse(published.Contains(L03BEvidenceProtocol.TrxArtifactPath, StringComparer.Ordinal),
                "A complete XML document with an unexecuted timeout TRX must not be published.");
            Assert.IsFalse(Directory.Exists(staging), "The real runner finally path must remove source staging, including its review key.");
            Assert.IsFalse(File.Exists(Path.Combine(temporaryRoot, "evidence-s-runner.lock")), "The runner lock must be released and removed.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
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
        StringAssert.Contains(script, "Assert-EvidenceSuccessMarker");
        StringAssert.Contains(script, "Test-CompletedEvidenceTrx -Path $_");
        StringAssert.Contains(script, "$failureStagingPath = \"$failureTerminalPath-publishing\"");
        StringAssert.Contains(script, "[IO.FileOptions]::DeleteOnClose");
        Assert.IsFalse(script.Contains("[string]$Nonce", StringComparison.Ordinal));

        int failureStart = script.IndexOf("function Publish-EvidenceFailure", StringComparison.Ordinal);
        int runnerStart = script.IndexOf("[void][IO.Directory]::CreateDirectory($localRoot)", failureStart, StringComparison.Ordinal);
        Assert.IsTrue(failureStart >= 0 && runnerStart > failureStart);
        string failurePublisher = script[failureStart..runnerStart];
        StringAssert.Contains(failurePublisher, "$commitmentsRelativePath");
        StringAssert.Contains(failurePublisher, "$attributionRelativePath");
        StringAssert.Contains(failurePublisher, "$trxRelativePath");
        StringAssert.Contains(failurePublisher, "$FailureStagingPath");
        Assert.IsFalse(failurePublisher.Contains("review-key", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(failurePublisher.Contains("success.json", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(failurePublisher.Contains("Get-ChildItem", StringComparison.Ordinal));
        Assert.IsFalse(failurePublisher.Contains("Move-Item -LiteralPath $StagingPath", StringComparison.Ordinal));
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
