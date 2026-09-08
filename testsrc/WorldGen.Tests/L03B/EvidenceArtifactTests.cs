using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class EvidenceArtifactTests
{
    // Non-normative project review policy frozen before the campaign. T03-06 specifies no numeric threshold.
    // These alarms only prove that every targeted fixture has signal and differs numerically from every other
    // fixture; they do not decide whether a human can recognize the intended morphology.
    private const double MinimumTargetVariance = 0.0001;
    private const double MinimumPairwiseMeanAbsoluteDifference = 0.035;
    private const int MinimumCorpusFamilies = 5;
    // Descriptive metric, deliberately not an acceptance threshold: a local extremum must differ from
    // every one of its eight neighbours by at least one percent of the sampled fixture range.
    private const double EightNeighborExtremaContrastFraction = .01;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [TestMethod]
    [DoNotParallelize]
    public void T0305AndT0306PublishAtomicBlindReviewEvidence()
    {
        string commit = RequireEnvironment("ISR_L03B_EVIDENCE_COMMIT", "^[0-9a-f]{40}$");
        string tree = RequireEnvironment("ISR_L03B_EVIDENCE_TREE", "^[0-9a-f]{40}$");
        string testAssemblyHash = RequireEnvironment("ISR_L03B_EVIDENCE_TEST_ASSEMBLY_SHA256", "^[0-9a-f]{64}$");
        string coreAssemblyHash = RequireEnvironment("ISR_L03B_EVIDENCE_CORE_ASSEMBLY_SHA256", "^[0-9a-f]{64}$");
        string fixturesBlob = RequireEnvironment("ISR_L03B_EVIDENCE_FIXTURES_BLOB", "^[0-9a-f]{40}$");
        string nonce = RequireEnvironment("ISR_L03B_EVIDENCE_NONCE", "^[0-9a-f]{64}$");
        string configuration = RequireEnvironment("ISR_L03B_EVIDENCE_CONFIGURATION", "^Release$");
        string runName = RequireEnvironment("ISR_L03B_EVIDENCE_RUN", $"^evidence-s-staging-{commit}-[0-9a-f]{{32}}$");
        string repository = L03BTestSupport.FindRepositoryRoot();
        if (runName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || runName.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Evidence run name must be a single safe directory name.");
        }
        ValidateProvenanceBeforeWriting(repository, commit, tree, fixturesBlob, configuration, testAssemblyHash, coreAssemblyHash);
        string output = Path.Combine(repository, ".local", "L03B", runName);
        if (Directory.Exists(output)) throw new InvalidOperationException("Evidence terminal directory must be absent before an atomic campaign starts.");
        (string Code, LandscapeFamily Family)[] blindOrder = DeriveBlindOrder(nonce);
        Directory.CreateDirectory(Path.Combine(output, "blind"));
        Directory.CreateDirectory(Path.Combine(output, "sealed"));
        string reportPath = Path.Combine(output, "sealed", "T03-05-06-S.json");
        string manifestPath = Path.Combine(output, "blind", "T03-06-S-manifest.json");
        string commitmentsPath = Path.Combine(output, L03BEvidenceProtocol.CommitmentsArtifactPath.Replace('/', Path.DirectorySeparatorChar));
        string keyPath = Path.Combine(output, "sealed", "T03-06-S-review-key.json");
        string successMarkerPath = Path.Combine(output, L03BEvidenceProtocol.SuccessMarkerArtifactPath.Replace('/', Path.DirectorySeparatorChar));
        byte[] commitmentsBytes = L03BEvidenceProtocol.CreateAttributionCommitments(
            runName, commit, tree, fixturesBlob, configuration, testAssemblyHash, coreAssemblyHash, blindOrder, nonce);
        WriteAtomic(commitmentsPath, commitmentsBytes);
        L03BBlindArtifact[] initialBlindArtifacts =
            [new(RelativeArtifactPath(output, commitmentsPath), L03BTestSupport.Sha256(commitmentsBytes))];
        WriteBlindManifest(manifestPath, commit, tree, fixturesBlob, configuration, testAssemblyHash, coreAssemblyHash,
            automatedStatus: "RUNNING", qualitativeReviewStatus: "REVIEW_REQUIRED", overallStatus: "RUNNING", initialBlindArtifacts);
        WriteAtomic(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            requirementIds = new[] { "R03-05", "R03-06" },
            automatedStatus = "RUNNING",
            qualitativeReviewStatus = "REVIEW_REQUIRED",
            overallStatus = "RUNNING",
            commit,
            tree,
            configuration,
        }, JsonOptions));

        try
        {
            RunCampaign(output, repository, reportPath, manifestPath, commit, tree, configuration, nonce, blindOrder,
                testAssemblyHash, coreAssemblyHash, fixturesBlob, commitmentsBytes, initialBlindArtifacts);
        }
        catch (Exception exception)
        {
            // The runner also excludes these paths structurally.  This best-effort
            // deletion closes the ordinary managed-exception path before it writes FAIL.
            TryDeleteSensitiveArtifact(keyPath);
            TryDeleteSensitiveArtifact(successMarkerPath);
            string failureAttributionPath = Path.Combine(output, "sealed", "T03-06-S-failure-attribution.json");
            string? failureAttributionSha256 = File.Exists(failureAttributionPath)
                ? L03BTestSupport.Sha256(File.ReadAllBytes(failureAttributionPath))
                : null;
            WriteAtomic(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                requirementIds = new[] { "R03-05", "R03-06" },
                automatedStatus = "FAIL",
                qualitativeReviewStatus = "NOT_RUN",
                overallStatus = "FAIL",
                commit,
                tree,
                configuration,
                failure = new { type = exception.GetType().FullName, exception.Message },
                selectiveFailureAttribution = failureAttributionSha256 is null ? null : new
                {
                    path = RelativeArtifactPath(output, failureAttributionPath),
                    sha256 = failureAttributionSha256,
                    protocol = "run-bound selective opening of one neutral code; no complete review key",
                },
            }, JsonOptions));
            WriteBlindManifest(manifestPath, commit, tree, fixturesBlob, configuration, testAssemblyHash, coreAssemblyHash,
                automatedStatus: "FAIL", qualitativeReviewStatus: "NOT_RUN", overallStatus: "FAIL", initialBlindArtifacts);
            throw;
        }
    }

    private static void RunCampaign(
        string output,
        string repository,
        string reportPath,
        string manifestPath,
        string commit,
        string tree,
        string configuration,
        string nonce,
        IReadOnlyList<(string Code, LandscapeFamily Family)> blindOrder,
        string testAssemblyHash,
        string coreAssemblyHash,
        string fixturesBlob,
        byte[] commitmentsBytes,
        IReadOnlyList<L03BBlindArtifact> initialBlindArtifacts)
    {
        string keyPath = Path.Combine(output, "sealed", "T03-06-S-review-key.json");
        string successMarkerPath = Path.Combine(output, L03BEvidenceProtocol.SuccessMarkerArtifactPath.Replace('/', Path.DirectorySeparatorChar));
        VerticalMetric[] verticalMetrics = new[] { "laboratory", "balanced", "vast-expeditions" }
            .Select(VerticalMetricFor)
            .ToArray();
        Assert.IsTrue(verticalMetrics.All(item => item.StrictlyMonotone));
        Assert.IsTrue(verticalMetrics.All(item => item.QuantizedNonDecreasing));

        FrozenScaleProfile laboratory = L03BTestSupport.FrozenProfile("laboratory");
        GenerationIdentity laboratoryIdentity = L03BTestSupport.Identity(73, laboratory);
        GenerationFailure<ReliefVerticalPlan> impossibleBelow = Failure(ReliefVerticalBudgetPlanner.Create(
            laboratoryIdentity,
            laboratory,
            new ReliefBudgetRequest(70, 40, 96)));
        GenerationFailure<ReliefVerticalPlan> impossibleAbove = Failure(ReliefVerticalBudgetPlanner.Create(
            laboratoryIdentity,
            laboratory,
            new ReliefBudgetRequest(28, 40, 130)));
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, impossibleBelow.Error.Code);
        Assert.AreEqual("geology.landscapes.vertical-below-sea", impossibleBelow.Error.Stage);
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, impossibleAbove.Error.Code);
        Assert.AreEqual("geology.landscapes.vertical-above-sea", impossibleAbove.Error.Stage);

        var fixtureMetrics = new List<BlindFixtureMetric>();
        var blindArtifacts = new List<L03BBlindArtifact>(initialBlindArtifacts);
        var sealedFixtures = new List<object>();
        var profileRows = new StringBuilder("code,direction,index,offset-x-blocks,offset-z-blocks,normalized-height\n");
        var blindViews = new List<object>();
        var sampledByCode = new Dictionary<string, double[,]>(StringComparer.Ordinal);
        string? activeCode = null;
        LandscapeFamily? activeFamily = null;
        try
        {
            foreach ((string code, LandscapeFamily family) in blindOrder)
            {
                activeCode = code;
                activeFamily = family;
                LandscapeFamilyProfile profile = LandscapeFamilyCatalog.Get(family);
                FixtureSample fixture = SampleFixture(family, profile);
                double[,] samples = L03BEvidenceViews.Altitudes(fixture.View);
                sampledByCode.Add(code, samples);
                BlindFixtureMetric metric = Measure(code, fixture.View, samples);
                fixtureMetrics.Add(metric);
                Assert.IsGreaterThan(MinimumTargetVariance, metric.Variance, code);
                Assert.IsTrue(double.IsFinite(metric.Mean) && double.IsFinite(metric.Variance) &&
                    double.IsFinite(metric.MeanAbsoluteSlope) && double.IsFinite(metric.RugosityLag1) &&
                    double.IsFinite(metric.RugosityLag4) && double.IsFinite(metric.RugosityLag12) &&
                    double.IsFinite(metric.ResidualPlanEnergy) && metric.Jumps.IsFinite &&
                    double.IsFinite(metric.DirectionalBalance) && metric.DirectionalBalance is >= 0 and <= 1 &&
                    metric.EightNeighborExtremaAtContrastFraction >= 0 && metric.SaturatedPixelCount >= 0,
                    $"Blind metrics must be finite and bounded where defined for {code}.");

                byte[] bitmap = RenderBitmap(fixture.View);
                string mapPath = Path.Combine(output, "blind", $"T03-06-{code}.bmp");
                WriteAtomic(mapPath, bitmap);
                blindArtifacts.Add(new L03BBlindArtifact(RelativeArtifactPath(output, mapPath), L03BTestSupport.Sha256(bitmap)));
                byte[] transitionMask = L03BEvidenceViews.RenderTransitionMask(fixture.View);
                string maskPath = Path.Combine(output, "blind", $"T03-06-{code}-transition-mask.pgm");
                WriteAtomic(maskPath, transitionMask);
                blindArtifacts.Add(new L03BBlindArtifact(RelativeArtifactPath(output, maskPath), L03BTestSupport.Sha256(transitionMask)));
                blindViews.Add(new
                {
                    code,
                    widthPixels = fixture.View.Side,
                    heightPixels = fixture.View.Side,
                    fixture.View.SpanBlocks,
                    nominalStepBlocks = fixture.View.StepBlocks,
                    xOffsetsBlocks = fixture.View.Pixels.Where(pixel => pixel.Row == fixture.View.Side / 2)
                        .OrderBy(pixel => pixel.Column).Select(pixel => pixel.X - fixture.View.CenterX),
                    zOffsetsBlocks = fixture.View.Pixels.Where(pixel => pixel.Column == fixture.View.Side / 2)
                        .OrderBy(pixel => pixel.Row).Select(pixel => pixel.Z - fixture.View.CenterZ),
                    transitionMask = new
                    {
                        path = RelativeArtifactPath(output, maskPath),
                        sha256 = L03BTestSupport.Sha256(transitionMask),
                        encoding = "PGM P5; 0=owner-pure, 255=transition/foreign contributor",
                        transitionPixelCount = 0,
                    },
                    purityContract = "every rendered altitude has one owner, IsTransition=false, contributorCount=1, primaryWeight=1, foreignWeight=0, foreignContribution=0",
                });
                sealedFixtures.Add(new
                {
                    code,
                    family = family.ToString(),
                    fixture.Seed,
                    site = fixture.SiteId.ToString(),
                    fixture.View.CenterX,
                    fixture.View.CenterZ,
                    fixture.View.SpanBlocks,
                    fixture.View.StepBlocks,
                });

                AppendProfiles(profileRows, code, fixture.View, samples);
            }
            activeCode = null;
            activeFamily = null;
        }
        catch
        {
            if (activeCode is not null && activeFamily is LandscapeFamily failedFamily)
            {
                byte[] receipt = L03BEvidenceProtocol.CreateFailureAttribution(
                    commitmentsBytes, activeCode, failedFamily, nonce);
                WriteAtomic(Path.Combine(output, "sealed", "T03-06-S-failure-attribution.json"), receipt);
            }
            throw;
        }

        for (int left = 0; left < blindOrder.Count; left++)
        {
            for (int right = left + 1; right < blindOrder.Count; right++)
            {
                Assert.IsGreaterThan(
                    MinimumPairwiseMeanAbsoluteDifference,
                    MeanAbsoluteDifference(
                        sampledByCode[blindOrder[left].Code],
                        sampledByCode[blindOrder[right].Code]),
                    $"{blindOrder[left].Code}/{blindOrder[right].Code}");
            }
        }

        byte[] profileBytes = Encoding.UTF8.GetBytes(profileRows.ToString());
        string profilesPath = Path.Combine(output, "blind", "T03-06-S-blind-profiles.csv");
        WriteAtomic(profilesPath, profileBytes);
        string metricsPath = Path.Combine(output, "blind", "T03-06-S-blind-metrics.json");
        byte[] metricsBytes = JsonSerializer.SerializeToUtf8Bytes(fixtureMetrics, JsonOptions);
        WriteAtomic(metricsPath, metricsBytes);
        string viewsPath = Path.Combine(output, "blind", "T03-06-S-blind-views.json");
        byte[] viewsBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            coordinateUnit = "blocks",
            requestedMacroSpanFactor = L03BEvidenceViews.RequestedMacroSpanFactor,
            minimumMacroSpanFactor = L03BEvidenceViews.MinimumMacroSpanFactor,
            selection = "largest declared span factor whose every sampled pixel passes the pure-owner contract; candidates and cells use stable order",
            grayscale = new { minimum = -1d, maximum = 1d, mapping = "common linear grayscale shared by every S view" },
            views = blindViews,
        }, JsonOptions);
        WriteAtomic(viewsPath, viewsBytes);
        blindArtifacts.Add(new L03BBlindArtifact(RelativeArtifactPath(output, profilesPath), L03BTestSupport.Sha256(profileBytes)));
        blindArtifacts.Add(new L03BBlindArtifact(RelativeArtifactPath(output, metricsPath), L03BTestSupport.Sha256(metricsBytes)));
        blindArtifacts.Add(new L03BBlindArtifact(RelativeArtifactPath(output, viewsPath), L03BTestSupport.Sha256(viewsBytes)));
        string reviewRequestPath = Path.Combine(output, "blind", "T03-06-S-review-request.json");
        byte[] reviewRequestBytes = L03BEvidenceProtocol.CreateBlindReviewRequest(blindOrder.Select(item => item.Code).ToArray());
        WriteAtomic(reviewRequestPath, reviewRequestBytes);
        blindArtifacts.Add(new L03BBlindArtifact(RelativeArtifactPath(output, reviewRequestPath), L03BTestSupport.Sha256(reviewRequestBytes)));
        byte[] fixturesBytes = L03BEvidenceProtocol.ReadVerifiedGitBlob(repository, fixturesBlob);
        FixtureCorpus fixtureCorpus = FixtureCorpus.Load(fixturesBytes);
        IReadOnlyList<int> calibrationSeeds = fixtureCorpus.CalibrationSeeds;
        IReadOnlyList<int> holdoutSeeds = fixtureCorpus.HoldoutSeeds;
        Assert.HasCount(192, calibrationSeeds);
        Assert.HasCount(64, holdoutSeeds);
        Assert.AreEqual(192, calibrationSeeds.Distinct().Count());
        Assert.AreEqual(64, holdoutSeeds.Distinct().Count());
        CollectionAssert.AreEquivalent(Array.Empty<int>(), calibrationSeeds.Intersect(holdoutSeeds).ToArray());
        FrozenScaleProfile balanced = L03BTestSupport.FrozenProfile("balanced");
        FrozenScaleProfile vast = L03BTestSupport.FrozenProfile("vast-expeditions");
        string progressPath = Path.Combine(output, "sealed", "T03-06-S-progress.json");
        var corpusEntries = new List<CorpusMetric>(256);
        foreach ((string corpusName, IReadOnlyList<int> seeds, FrozenScaleProfile corpusProfile) in
                 new[] { ("calibration", calibrationSeeds, balanced), ("holdout", holdoutSeeds, vast) })
        {
            foreach (int seed in seeds)
            {
                WriteCampaignProgress(progressPath, corpusName, seed, "atlas");
                CorpusMetric metric = CorpusMetricFor(corpusName, seed, corpusProfile, stage =>
                    WriteCampaignProgress(progressPath, corpusName, seed, stage));
                corpusEntries.Add(metric);
                WriteCampaignProgress(progressPath, corpusName, seed, "complete");
            }
        }
        CorpusMetric[] corpus = corpusEntries.ToArray();
        Assert.HasCount(256, corpus);
        Assert.AreEqual(256, corpus.Select(item => (item.Corpus, item.Seed)).Distinct().Count());
        int corpusFamilyCount = corpus.SelectMany(item => item.CellFamilyCounts.Keys).Distinct().Count();
        Assert.IsGreaterThanOrEqualTo(MinimumCorpusFamilies, corpusFamilyCount);
        Assert.IsTrue(corpus.All(item => item.MinimumModelAltitude is >= -1 and <= 1));
        Assert.IsTrue(corpus.All(item => item.MaximumModelAltitude is >= -1 and <= 1));
        Assert.IsTrue(corpus.All(item => item.MinimumAltitudeBlocks >= item.DeepestOceanFloorBlocks));
        Assert.IsTrue(corpus.All(item => item.MaximumAltitudeBlocks <= item.HighestReliefBlocks));
        Assert.IsTrue(corpus.Any(item => item.MaximumBathymetryBlocks > 0));
        TraitFailureRate[] traitFailureRates = corpus
            .SelectMany(seed => seed.FamilyMorphology.SelectMany(family => family.Traits.Select(trait => new
            {
                seed.Corpus,
                seed.Seed,
                family.Family,
                Trait = trait,
            })))
            .GroupBy(item => (item.Corpus, item.Family, item.Trait.Name))
            .OrderBy(group => group.Key.Corpus, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Family, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Name, StringComparer.Ordinal)
            .Select(group => new TraitFailureRate(
                group.Key.Corpus,
                group.Key.Family,
                group.Key.Name,
                group.Count(),
                group.Count(item => !item.Trait.Passed),
                group.Count(item => item.Trait.Passed),
                group.Count(item => !item.Trait.Passed) / (double)group.Count(),
                group.Where(item => !item.Trait.Passed).Select(item => item.Seed).Order().ToArray()))
            .ToArray();

        byte[]? keyBytes = null;
        try
        {
            object report = new
            {
                schemaVersion = 1,
                requirementIds = new[] { "R03-05", "R03-06" },
                automatedStatus = "PASS",
                qualitativeReviewStatus = "REVIEW_REQUIRED",
                overallStatus = "REVIEW_REQUIRED",
                reason = "T03-06 requires independent recognition review; numeric thresholds are a predeclared non-normative alarm only.",
                commit,
                tree,
                configuration,
                targetFramework = "net10.0",
                runtime = new { runtimeVersion = Environment.Version.ToString(), os = Environment.OSVersion.VersionString },
                frozenFixtureCorpus = new
                {
                    calibrationSeeds,
                    holdoutSeeds,
                    fixturesBlob,
                    fixturesSha256 = L03BTestSupport.Sha256(fixturesBytes),
                },
                provisionalMetricPolicy = new
                {
                    minimumTargetVariance = MinimumTargetVariance,
                    minimumPairwiseMeanAbsoluteDifference = MinimumPairwiseMeanAbsoluteDifference,
                    minimumCorpusFamilies = MinimumCorpusFamilies,
                    justification = "Signal-presence, pairwise-difference and broad-corpus diversity alarms; none is a normative T03-06 recognition threshold.",
                    declaredBeforeCampaign = true,
                },
                t0305 = new
                {
                    verticalMetrics,
                    impossiblePresets = new[]
                {
                    new { impossibleBelow.Error.Code, impossibleBelow.Error.Stage, impossibleBelow.Error.Details },
                    new { impossibleAbove.Error.Code, impossibleAbove.Error.Stage, impossibleAbove.Error.Details },
                },
                    mapping = "piecewise linear, model -1..0 => deepest ocean..sea; 0..1 => sea..highest relief; no output clamp",
                    quantization = "shared HeightQuantizer v1, 1/256 block; non-decreasing after quantization",
                },
                t0306 = new
                {
                    blindReview = new
                    {
                        maps = blindArtifacts.Where(item => item.Path.EndsWith(".bmp", StringComparison.Ordinal)),
                        profiles = new
                        {
                            path = RelativeArtifactPath(output, profilesPath),
                            sha256 = L03BTestSupport.Sha256(profileBytes),
                        },
                        blindMetrics = new { path = RelativeArtifactPath(output, metricsPath), sha256 = L03BTestSupport.Sha256(metricsBytes) },
                        viewGeometryAndTransitionMasks = new { path = RelativeArtifactPath(output, viewsPath), sha256 = L03BTestSupport.Sha256(viewsBytes) },
                        reviewProtocol = new
                        {
                            requestPath = RelativeArtifactPath(output, reviewRequestPath),
                            requestSha256 = L03BTestSupport.Sha256(reviewRequestBytes),
                            requiredPrerevealFields = new[] { "identifiedFamilyBeforeReveal", "confidence0To100BeforeReveal", "morphologyObservations" },
                            reviewerCommand = "New-L03BBlindReviewReceipt.ps1 -BlindDirectory <terminal/blind> -AnswersPath <answers.json> [-ReviewParent <canonical-terminal-parent>]",
                            controllerCommand = "Open-L03BBlindReview.ps1 -EvidenceDirectory <terminal> -ExpectedReceiptSha256 <reviewer-reported-sha256>",
                            trustBoundary = "The reviewer receives only blind/. The controller retains sealed/ and the reveal tool rejects absent, changed, replayed, or mismatched receipts before parsing the key.",
                        },
                        separateKey = new
                        {
                            path = RelativeArtifactPath(output, keyPath),
                            instruction = "Do not read directly. Only Open-L03BBlindReview.ps1 may parse this key after verifying a durable prereveal receipt.",
                        },
                        attributionCommitments = new
                        {
                            path = "blind/T03-06-S-attribution-commitments.json",
                            sha256 = L03BTestSupport.Sha256(commitmentsBytes),
                            protocol = L03BEvidenceProtocol.FailureAttributionScheme,
                        },
                        fixtureMetrics,
                    },
                    progress = new
                    {
                        path = RelativeArtifactPath(output, progressPath),
                        sha256 = L03BTestSupport.Sha256(File.ReadAllBytes(progressPath)),
                        protocol = "atomic seed/stage heartbeat; the terminal corpus report preserves each of the 256 seeds once.",
                    },
                    measures = "pure-owner samples only: mean, variance, slope, multi-scale roughness, residual energy after planar detrend, eight-neighbor extrema at a documented contrast fraction, line/column/diagonal jumps, directional balance and saturation; descriptive only, not T03-06 thresholds",
                    grayscale = new { minimum = -1d, maximum = 1d, mapping = "common linear grayscale shared by every S view" },
                    unfilteredCorpus = corpus,
                    corpusFamilyCount,
                    traitFailureRates,
                    traitEvaluationStatus = traitFailureRates.All(item => item.Failures == 0) ? "ALL_PASSED" : "OBSERVED_FAILURES_RETAINED",
                    traitPolicy = "Every measured family/seed retains each individual outcome. Rates are failures/attempts; no median or percentile decides a trait.",
                    sealedFixtures,
                },
            };

            byte[] reportBytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
            byte[] blindManifestBytes = L03BEvidenceProtocol.CreateBlindManifest(
                L03BEvidenceProtocol.ManifestSchemaVersion,
                "PASS",
                "REVIEW_REQUIRED",
                "REVIEW_REQUIRED",
                commit,
                tree,
                fixturesBlob,
                configuration,
                testAssemblyHash,
                coreAssemblyHash,
                blindArtifacts);

            // The complete mapping is materialized only after every corpus item,
            // campaign assertion, report and final blind manifest have completed.
            // It binds the exact manifest that the external reviewer must hash.
            keyBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 3,
                status = "SEALED_AWAITING_VERIFIED_RECEIPT",
                protocol = L03BEvidenceProtocol.BlindReviewReceiptScheme,
                binding = new
                {
                    runId = Path.GetFileName(output),
                    commit,
                    tree,
                    fixturesBlob,
                    configuration,
                    testAssemblySha256 = testAssemblyHash,
                    coreAssemblySha256 = coreAssemblyHash,
                },
                blindManifestSha256 = L03BTestSupport.Sha256(blindManifestBytes),
                attributionCommitmentsSha256 = L03BTestSupport.Sha256(commitmentsBytes),
                nonce,
                entries = blindOrder.OrderBy(item => item.Code, StringComparer.Ordinal)
                    .Select(item => new { item.Code, family = item.Family.ToString() }),
            }, JsonOptions);
            byte[] successMarkerBytes = L03BEvidenceProtocol.CreateSuccessMarker(
                Path.GetFileName(output),
                commit,
                tree,
                fixturesBlob,
                testAssemblyHash,
                coreAssemblyHash,
                L03BTestSupport.Sha256(reportBytes),
                L03BTestSupport.Sha256(blindManifestBytes),
                L03BTestSupport.Sha256(keyBytes),
                L03BTestSupport.Sha256(commitmentsBytes));
            WriteAtomic(reportPath, reportBytes);
            WriteAtomic(manifestPath, blindManifestBytes);
            WriteAtomic(successMarkerPath, successMarkerBytes);
            // The complete key is the last fallible campaign operation.  The
            // runner accepts it only after a clean test exit, a complete PASS TRX,
            // and verification of the earlier marker's exact artifact hashes.
            WriteAtomic(keyPath, keyBytes);
        }
        finally
        {
            if (keyBytes is not null) CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static VerticalMetric VerticalMetricFor(string profileId)
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile(profileId);
        ReliefBudgetRequest request = profileId == "laboratory"
            ? new ReliefBudgetRequest(28, 40, 96)
            : new ReliefBudgetRequest(64, 48, 128);
        ReliefVerticalPlan plan = L03BTestSupport.Success(ReliefVerticalBudgetPlanner.Create(
            L03BTestSupport.Identity(73, profile),
            profile,
            request));
        bool strictlyMonotone = true;
        bool quantizedNonDecreasing = true;
        double previous = plan.Transform.MapModelAltitudeToBlocks(-1);
        long previousQuantized = plan.Transform.MapModelAltitudeToQuantizedBlocks(-1);
        for (int index = 1; index <= 4_000; index++)
        {
            double value = -1 + (index / 2_000d);
            double mapped = plan.Transform.MapModelAltitudeToBlocks(value);
            long quantized = plan.Transform.MapModelAltitudeToQuantizedBlocks(value);
            strictlyMonotone &= mapped > previous;
            quantizedNonDecreasing &= quantized >= previousQuantized;
            previous = mapped;
            previousQuantized = quantized;
        }

        return new VerticalMetric(
            profile.Id,
            profile.ProfileVersion,
            profile.GeographyConfigHash.ToString(),
            profile.HeightBlocks,
            profile.VerticalEnvelope.SeaLevel,
            profile.VerticalEnvelope.MinimumFloorThickness,
            profile.VerticalEnvelope.MinimumCavernCover,
            profile.VerticalEnvelope.HeightMargin,
            plan.MaximumOceanDepthBlocks,
            plan.MinimumCavernInteriorHeightBlocks,
            plan.MaximumReliefAboveSeaBlocks,
            plan.RockFloorTopBlocks,
            plan.DeepestOceanFloorBlocks,
            plan.MaximumCavernCeilingBlocks,
            plan.HighestReliefBlocks,
            strictlyMonotone,
            quantizedNonDecreasing);
    }

    private static CorpusMetric CorpusMetricFor(string corpus, int seed, FrozenScaleProfile profile, Action<string>? progress = null)
    {
        GenerationIdentity identity = L03BTestSupport.Identity(seed, profile);
        progress?.Invoke("atlas");
        (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(seed, profile, progress);
        progress?.Invoke("landscape");
        ReliefBudgetRequest budget = new(64, 48, 128);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(
            identity,
            atlas,
            plates,
            profile,
            new LandscapeGenerationSettings(budget, profile.SiteQuota, 1.25)));
        var samples = new List<LandscapeSample>();
        progress?.Invoke("samples");
        for (int z = 1; z <= 8; z++)
        {
            for (int x = 1; x <= 8; x++)
            {
                samples.Add(model.Sample(
                    (profile.WidthBlocks * x) / 9,
                    (profile.LengthBlocks * z) / 9));
            }
        }

        var measurements = new Dictionary<LandscapeFamily, L03BMorphologyMeasurement>();
        var views = new Dictionary<LandscapeFamily, L03BPureLandscapeView>();
        foreach (LandscapeFamily family in Enum.GetValues<LandscapeFamily>())
        {
            L03BPureLandscapeView? view = L03BEvidenceViews.TrySelectPureView(
                model, atlas, family, seed, L03BEvidenceViews.CorpusMapSide);
            if (view is null)
            {
                continue;
            }
            views.Add(family, view);
            measurements.Add(family, L03BEvidenceViews.MeasureMorphology(view));
        }
        SeedFamilyMorphology[] familyMorphology = Enum.GetValues<LandscapeFamily>()
            .Select(family => FamilyMorphologyForSeed(family, model, views, measurements))
            .ToArray();

        return new CorpusMetric(
            corpus,
            seed,
            profile.Id,
            profile.GeographyConfigHash.ToString(),
            plates.ContentChecksum.ToString(),
            model.ContentChecksum.ToString(),
            model.Cells.GroupBy(item => item.Family).OrderBy(group => group.Key)
                .ToDictionary(group => group.Key.ToString(), group => group.Count(), StringComparer.Ordinal),
            samples.Min(item => item.ModelAltitudeNormalized),
            samples.Max(item => item.ModelAltitudeNormalized),
            samples.Min(item => item.AltitudeBlocks),
            samples.Max(item => item.AltitudeBlocks),
            samples.Max(item => item.BathymetryBlocks),
            model.VerticalPlan.DeepestOceanFloorBlocks,
            model.VerticalPlan.HighestReliefBlocks,
            familyMorphology);
    }

    private static SeedFamilyMorphology FamilyMorphologyForSeed(
        LandscapeFamily family,
        LandscapeModel model,
        IReadOnlyDictionary<LandscapeFamily, L03BPureLandscapeView> views,
        IReadOnlyDictionary<LandscapeFamily, L03BMorphologyMeasurement> measurements)
    {
        int cellCount = model.Cells.Count(cell => cell.Family == family);
        if (cellCount == 0)
        {
            return new SeedFamilyMorphology(family.ToString(), cellCount, "FAMILY_ABSENT", null, null, []);
        }
        if (!views.TryGetValue(family, out L03BPureLandscapeView? view) ||
            !measurements.TryGetValue(family, out L03BMorphologyMeasurement? metric))
        {
            return new SeedFamilyMorphology(
                family.ToString(),
                cellCount,
                "NO_PURE_VIEW",
                null,
                null,
                [new ExpectedTrait("pure-owner-view-available", 0d, ">=", 1d, false)]);
        }

        ExpectedTrait[] traits = ExpectedTraits(family, metric, measurements);
        return new SeedFamilyMorphology(
            family.ToString(),
            cellCount,
            "MEASURED",
            new PureViewGeometry(
                view.OwnerCellId.ToString(),
                view.CenterX,
                view.CenterZ,
                view.Side,
                view.SpanBlocks,
                view.StepBlocks,
                view.SpanBlocks / LandscapeFamilyCatalog.Get(family).MacroWavelengthBlocks,
                0),
            metric,
            traits);
    }

    private static ExpectedTrait[] ExpectedTraits(
        LandscapeFamily family,
        L03BMorphologyMeasurement metric,
        IReadOnlyDictionary<LandscapeFamily, L03BMorphologyMeasurement> all)
    {
        var traits = new List<ExpectedTrait>();
        switch (family)
        {
            case LandscapeFamily.RuggedRanges:
                traits.Add(Trait("aligned-ridge-anisotropy", metric.GradientAnisotropy, ">", .6d));
                traits.Add(Trait("multiple-prominent-peaks", metric.ProminentPeaks, ">=", 2d));
                AddRatio("anisotropy-vs-old-massifs", metric.GradientAnisotropy, all, LandscapeFamily.OldMassifs, value => value.GradientAnisotropy, 1.5d);
                break;
            case LandscapeFamily.OldMassifs:
                traits.Add(Trait("multiple-rounded-summits", metric.ProminentPeaks, ">=", 2d));
                traits.Add(Trait("rounded-valley", metric.ProminentValleys, ">=", 1d));
                if (all.TryGetValue(LandscapeFamily.RuggedRanges, out L03BMorphologyMeasurement? ranges))
                {
                    traits.Add(Trait("less-directional-than-ranges", Ratio(ranges.GradientAnisotropy, metric.GradientAnisotropy), ">", 1.5d));
                }
                break;
            case LandscapeFamily.Plateaus:
                traits.Add(Trait("high-flat-interior-fraction", metric.HighFlatFraction, ">", .75d));
                traits.Add(Trait("escarpment-curvature-contrast", Ratio(metric.Curvature90, metric.MedianCurvature), ">", 8d));
                AddRatio("elevated-flatness-vs-plains", metric.HighFlatFraction, all, LandscapeFamily.Plains, value => value.HighFlatFraction, 1d);
                break;
            case LandscapeFamily.SedimentaryBasins:
                traits.Add(Trait("closed-floor-broad-rim", Ratio(metric.BroadRimLift, metric.NominalReliefIncrement), ">", 20d));
                AddRatio("rim-lift-vs-plains", metric.BroadRimLift, all, LandscapeFamily.Plains, value => value.BroadRimLift, 4d);
                break;
            case LandscapeFamily.Plains:
                L03BMorphologyMeasurement[] others = all.Where(pair => pair.Key != LandscapeFamily.Plains).Select(pair => pair.Value).ToArray();
                if (others.Length > 0)
                {
                    traits.Add(Trait("lowest-physical-slope", Ratio(others.Min(value => value.PhysicalSlope), metric.PhysicalSlope), ">", 1d));
                    traits.Add(Trait("lowest-physical-curvature", Ratio(others.Min(value => value.PhysicalCurvature), metric.PhysicalCurvature), ">", 1d));
                }
                break;
            case LandscapeFamily.VolcanicDomains:
                traits.Add(Trait("caldera-rim-lift", Ratio(metric.CraterLift, metric.NominalReliefIncrement), ">", 2d));
                traits.Add(Trait("outer-cone-drop", Ratio(metric.ConeDrop, metric.NominalReliefIncrement), ">", 2.5d));
                traits.Add(Trait("separated-summits", metric.ProminentPeaks, ">=", 2d));
                traits.Add(Trait("caldera-valleys", metric.ProminentValleys, ">=", 2d));
                AddRatio("caldera-lift-vs-plains", metric.CraterLift, all, LandscapeFamily.Plains, value => value.CraterLift, 1d);
                AddRatio("summits-vs-plains", metric.ProminentPeaks, all, LandscapeFamily.Plains, value => value.ProminentPeaks, 1d);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family));
        }
        return traits.ToArray();

        void AddRatio(
            string name,
            double numerator,
            IReadOnlyDictionary<LandscapeFamily, L03BMorphologyMeasurement> source,
            LandscapeFamily denominatorFamily,
            Func<L03BMorphologyMeasurement, double> denominator,
            double expected)
        {
            if (source.TryGetValue(denominatorFamily, out L03BMorphologyMeasurement? other))
            {
                traits.Add(Trait(name, Ratio(numerator, denominator(other)), ">", expected));
            }
        }
    }

    private static ExpectedTrait Trait(string name, double observed, string comparison, double expected) =>
        new(name, observed, comparison, expected, comparison == ">=" ? observed >= expected : observed > expected);

    private static double Ratio(double numerator, double denominator) => denominator == 0d
        ? (numerator > 0d ? double.MaxValue : 0d)
        : numerator / denominator;

    private static void WriteCampaignProgress(string path, string corpus, int seed, string stage) =>
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(new { corpus, seed, stage }, JsonOptions));

    private static FixtureSample SampleFixture(LandscapeFamily family, LandscapeFamilyProfile profile)
    {
        // Targeted maps traverse the published composition, but only after the
        // selector has proved every rendered pixel belongs to one pure owner core.
        int[] fixtureSeeds = [20260907, -20260907, 731, -731, 196883, -196883, 48731, -48731];
        FrozenScaleProfile frozen = L03BTestSupport.FrozenProfile("vast-expeditions");
        foreach (int seed in fixtureSeeds)
        {
            GenerationIdentity identity = L03BTestSupport.Identity(seed, frozen);
            (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(seed, frozen);
            LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, frozen,
                new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), frozen.SiteQuota, 1.25)));
            L03BPureLandscapeView? view = L03BEvidenceViews.TrySelectPureView(
                model, atlas, family, seed, L03BEvidenceViews.BlindMapSide);
            if (view is not null)
            {
                return new FixtureSample(view, seed, view.OwnerCellId);
            }
        }
        throw new AssertFailedException(
            $"No declared targeted fixture contains a pure owner view for family {family} at the declared minimum span {profile.MacroWavelengthBlocks * L03BEvidenceViews.MinimumMacroSpanFactor:R} blocks.");
    }

    private static BlindFixtureMetric Measure(string code, L03BPureLandscapeView view, double[,] samples)
    {
        L03BEvidenceViews.RequirePure(view);
        double[] values = samples.Cast<double>().ToArray();
        double mean = values.Average();
        double variance = values.Select(value => (value - mean) * (value - mean)).Average();
        DirectionalJumpStatistics jumps = AdjacentJumpStatistics(samples);
        return new BlindFixtureMetric(
            code,
            view.Side,
            view.SpanBlocks,
            view.StepBlocks,
            0,
            mean,
            variance,
            MeanAbsoluteSlope(samples),
            Roughness(samples, 1),
            Roughness(samples, 4),
            Roughness(samples, 12),
            ResidualPlanEnergy(samples),
            EightNeighborExtremaAtContrastFraction(samples),
            jumps,
            DirectionalBalance(samples),
            samples.Cast<double>().Count(value => Math.Abs(value) >= .999));
    }

    private static void AppendProfiles(StringBuilder rows, string code, L03BPureLandscapeView view, double[,] samples)
    {
        int width = samples.GetLength(1);
        int height = samples.GetLength(0);
        AppendProfile("horizontal", index => (height / 2, index), width);
        AppendProfile("vertical", index => (index, width / 2), height);
        AppendProfile("diagonal-main", index => (index, index), Math.Min(width, height));
        AppendProfile("diagonal-anti", index => (index, width - 1 - index), Math.Min(width, height));

        void AppendProfile(string kind, Func<int, (int Row, int Column)> location, int length)
        {
            for (int index = 0; index < length; index++)
            {
                (int row, int column) = location(index);
                L03BEvidencePixel pixel = view.Pixels.Single(item => item.Row == row && item.Column == column);
                rows.Append(code).Append(',').Append(kind).Append(',')
                    .Append(index).Append(',')
                    .Append(pixel.X - view.CenterX).Append(',')
                    .Append(pixel.Z - view.CenterZ).Append(',')
                    .Append(samples[row, column].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            }
        }
    }

    private static double MeanAbsoluteSlope(double[,] samples) => Roughness(samples, 1);

    private static double ResidualPlanEnergy(double[,] samples)
    {
        int width = samples.GetLength(1);
        int height = samples.GetLength(0);
        double mean = samples.Cast<double>().Average();
        double meanX = (width - 1) / 2d;
        double meanZ = (height - 1) / 2d;
        double varianceX = Enumerable.Range(0, width).Select(x => (x - meanX) * (x - meanX)).Sum() * height;
        double varianceZ = Enumerable.Range(0, height).Select(z => (z - meanZ) * (z - meanZ)).Sum() * width;
        double slopeX = 0;
        double slopeZ = 0;
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
        {
            slopeX += (x - meanX) * (samples[z, x] - mean);
            slopeZ += (z - meanZ) * (samples[z, x] - mean);
        }
        slopeX /= varianceX;
        slopeZ /= varianceZ;
        double energy = 0;
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
        {
            double residual = samples[z, x] - (mean + (slopeX * (x - meanX)) + (slopeZ * (z - meanZ)));
            energy += residual * residual;
        }
        return energy / samples.Length;
    }

    private static int EightNeighborExtremaAtContrastFraction(double[,] samples)
    {
        int count = 0;
        double prominence = (samples.Cast<double>().Max() - samples.Cast<double>().Min()) * EightNeighborExtremaContrastFraction;
        for (int z = 1; z < samples.GetLength(0) - 1; z++) for (int x = 1; x < samples.GetLength(1) - 1; x++)
        {
            double value = samples[z, x];
            bool greater = true;
            bool less = true;
            double highestNeighbor = double.NegativeInfinity;
            double lowestNeighbor = double.PositiveInfinity;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dz != 0)
            {
                double neighbor = samples[z + dz, x + dx];
                greater &= value > neighbor;
                less &= value < neighbor;
                highestNeighbor = Math.Max(highestNeighbor, neighbor);
                lowestNeighbor = Math.Min(lowestNeighbor, neighbor);
            }
            if ((greater && value - highestNeighbor >= prominence) || (less && lowestNeighbor - value >= prominence)) count++;
        }
        return count;
    }

    private static DirectionalJumpStatistics AdjacentJumpStatistics(double[,] samples)
    {
        var horizontal = new List<double>(); var vertical = new List<double>(); var main = new List<double>(); var anti = new List<double>();
        for (int z = 0; z < samples.GetLength(0); z++) for (int x = 0; x < samples.GetLength(1); x++)
        {
            if (x + 1 < samples.GetLength(1)) horizontal.Add(Math.Abs(samples[z, x] - samples[z, x + 1]));
            if (z + 1 < samples.GetLength(0)) vertical.Add(Math.Abs(samples[z, x] - samples[z + 1, x]));
            if (x + 1 < samples.GetLength(1) && z + 1 < samples.GetLength(0)) main.Add(Math.Abs(samples[z, x] - samples[z + 1, x + 1]));
            if (x > 0 && z + 1 < samples.GetLength(0)) anti.Add(Math.Abs(samples[z, x] - samples[z + 1, x - 1]));
        }
        return new DirectionalJumpStatistics(Jump(horizontal), Jump(vertical), Jump(main), Jump(anti));
    }

    private static JumpStatistics Jump(List<double> values)
    {
        values.Sort();
        double maximum = values[^1];
        double p95 = values[(int)Math.Floor(.95 * (values.Count - 1))];
        return new JumpStatistics(maximum, p95, maximum == 0 ? 0 : p95 / maximum);
    }

    // One means equal mean horizontal and vertical change; zero means one direction has no change.
    private static double DirectionalBalance(double[,] samples)
    {
        double horizontal = 0;
        double vertical = 0;
        int horizontalCount = 0;
        int verticalCount = 0;
        for (int z = 0; z < samples.GetLength(0); z++) for (int x = 1; x < samples.GetLength(1); x++) { horizontal += Math.Abs(samples[z, x] - samples[z, x - 1]); horizontalCount++; }
        for (int z = 1; z < samples.GetLength(0); z++) for (int x = 0; x < samples.GetLength(1); x++) { vertical += Math.Abs(samples[z, x] - samples[z - 1, x]); verticalCount++; }
        if (horizontalCount == 0 || verticalCount == 0) return 0;
        double horizontalMean = horizontal / horizontalCount;
        double verticalMean = vertical / verticalCount;
        double maximum = Math.Max(horizontalMean, verticalMean);
        return maximum == 0 ? 0 : Math.Min(horizontalMean, verticalMean) / maximum;
    }

    private static double Roughness(double[,] samples, int lag)
    {
        double total = 0;
        int count = 0;
        for (int z = 0; z < samples.GetLength(0); z++)
        {
            for (int x = lag; x < samples.GetLength(1); x++)
            {
                total += Math.Abs(samples[z, x] - samples[z, x - lag]);
                count++;
            }
        }

        for (int z = lag; z < samples.GetLength(0); z++)
        {
            for (int x = 0; x < samples.GetLength(1); x++)
            {
                total += Math.Abs(samples[z, x] - samples[z - lag, x]);
                count++;
            }
        }

        return total / count;
    }

    private static double MeanAbsoluteDifference(double[,] left, double[,] right)
    {
        double total = 0;
        int count = left.Length;
        for (int z = 0; z < left.GetLength(0); z++)
        {
            for (int x = 0; x < left.GetLength(1); x++)
            {
                total += Math.Abs(left[z, x] - right[z, x]);
            }
        }

        return total / count;
    }

    private static string RequireEnvironment(string name, string expression)
    {
        string value = Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Evidence requires {name}.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, expression, System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException($"Evidence value {name} has an invalid format.");
        }
        return value;
    }

    private static (string Code, LandscapeFamily Family)[] DeriveBlindOrder(string nonce)
    {
        return Enum.GetValues<LandscapeFamily>()
            .Select(family => new
            {
                Family = family,
                Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"ISRW-L03B-S|{nonce}|{(int)family}"))),
            })
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select((item, index) => ($"S{index + 1:00}", item.Family))
            .ToArray();
    }

    private static void ValidateProvenanceBeforeWriting(string repository, string commit, string tree, string fixturesBlob, string configuration, string testAssemblyHash, string coreAssemblyHash)
    {
        int detachedExitCode = GitExitCode(repository, "symbolic-ref", "-q", "HEAD");
        if (!string.Equals(Git(repository, "rev-parse", "HEAD"), commit, StringComparison.Ordinal) ||
            !string.Equals(Git(repository, "rev-parse", "HEAD^{tree}"), tree, StringComparison.Ordinal) ||
            !string.Equals(Git(repository, "rev-parse", "HEAD:registry/fixtures.json"), fixturesBlob, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(Git(repository, "status", "--porcelain", "--untracked-files=all")) ||
            detachedExitCode != 1)
        {
            throw new InvalidOperationException("Evidence requires detached HEAD at the requested commit/tree and a fully clean working tree.");
        }

        Assembly assembly = typeof(EvidenceArtifactTests).Assembly;
        string? assemblyConfiguration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.Equals(configuration, "Release", StringComparison.Ordinal) ||
            !string.Equals(assemblyConfiguration, "Release", StringComparison.Ordinal) ||
            informational is null || !informational.EndsWith($"+{commit}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Evidence requires a Release assembly with informational version suffix +commit.");
        }
        if (!string.Equals(L03BTestSupport.Sha256(File.ReadAllBytes(assembly.Location)), testAssemblyHash, StringComparison.Ordinal) ||
            !string.Equals(L03BTestSupport.Sha256(File.ReadAllBytes(typeof(LandscapeModel).Assembly.Location)), coreAssemblyHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Evidence assembly hashes do not match the runner preflight.");
        }
    }

    private static string Git(string repository, params string[] arguments)
    {
        GitResult result = RunGit(repository, arguments);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Git provenance check failed: {result.StandardError.Trim()}");
        return result.StandardOutput.Trim();
    }

    private static int GitExitCode(string repository, params string[] arguments)
    {
        return RunGit(repository, arguments).ExitCode;
    }

    private static GitResult RunGit(string repository, IReadOnlyList<string> arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add($"safe.directory={repository.Replace('\\', '/')}");
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
        try
        {
            completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        catch (TimeoutException exception)
        {
            TerminateAndDrain(process, stdout, stderr);
            throw new TimeoutException("Git provenance check timed out.", exception);
        }
        catch (Exception exception)
        {
            TerminateAndDrain(process, stdout, stderr);
            throw new InvalidOperationException("Git provenance check failed while collecting process output.", exception);
        }
        return new GitResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static void TerminateAndDrain(Process process, Task<string> stdout, Task<string> stderr)
    {
        TryKill(process);
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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }

    private static byte[] RenderBitmap(L03BPureLandscapeView view)
    {
        double[,] samples = L03BEvidenceViews.Altitudes(view);
        int width = samples.GetLength(1);
        int height = samples.GetLength(0);
        int stride = checked(((width * 3) + 3) & ~3);
        int imageBytes = checked(stride * height);
        byte[] bitmap = new byte[checked(54 + imageBytes)];
        bitmap[0] = (byte)'B';
        bitmap[1] = (byte)'M';
        WriteInt32(bitmap, 2, bitmap.Length);
        WriteInt32(bitmap, 10, 54);
        WriteInt32(bitmap, 14, 40);
        WriteInt32(bitmap, 18, width);
        WriteInt32(bitmap, 22, height);
        bitmap[26] = 1;
        bitmap[28] = 24;
        WriteInt32(bitmap, 34, imageBytes);
        for (int z = 0; z < height; z++)
        {
            int row = 54 + ((height - 1 - z) * stride);
            for (int x = 0; x < width; x++)
            {
                int shade = checked((int)Math.Round((samples[z, x] + 1) * 127.5, MidpointRounding.ToEven));
                byte channel = checked((byte)shade);
                int offset = row + (x * 3);
                bitmap[offset] = channel;
                bitmap[offset + 1] = channel;
                bitmap[offset + 2] = channel;
            }
        }

        return bitmap;
    }

    private static GenerationFailure<T> Failure<T>(GenerationResult<T> result)
        where T : class
    {
        Assert.IsInstanceOfType<GenerationFailure<T>>(result);
        return (GenerationFailure<T>)result;
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void TryDeleteSensitiveArtifact(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // The runner never copies this path into a failure publication and
            // destroys the entire source staging directory in its finally block.
        }
        catch (UnauthorizedAccessException)
        {
            // Same structural containment as the IOException path above.
        }
    }

    private static string RelativeArtifactPath(string output, string path) =>
        Path.GetRelativePath(output, path).Replace('\\', '/');

    private static void WriteBlindManifest(
        string manifestPath,
        string commit,
        string tree,
        string fixturesBlob,
        string configuration,
        string testAssemblyHash,
        string coreAssemblyHash,
        string automatedStatus,
        string qualitativeReviewStatus,
        string overallStatus,
        IReadOnlyList<L03BBlindArtifact> artifacts)
    {
        WriteAtomic(manifestPath, L03BEvidenceProtocol.CreateBlindManifest(
            L03BEvidenceProtocol.ManifestSchemaVersion,
            automatedStatus,
            qualitativeReviewStatus,
            overallStatus,
            commit,
            tree,
            fixturesBlob,
            configuration,
            testAssemblyHash,
            coreAssemblyHash,
            artifacts));
    }

    private static void WriteInt32(byte[] destination, int offset, int value)
    {
        destination[offset] = unchecked((byte)value);
        destination[offset + 1] = unchecked((byte)(value >> 8));
        destination[offset + 2] = unchecked((byte)(value >> 16));
        destination[offset + 3] = unchecked((byte)(value >> 24));
    }

    private sealed record VerticalMetric(
        string ProfileId,
        uint ProfileVersion,
        string GeographyConfigHash,
        int WorldHeightBlocks,
        long SeaLevelBlocks,
        long RockFloorThicknessBlocks,
        long CavernCoverBlocks,
        long UpperMarginBlocks,
        long MaximumOceanDepthBlocks,
        long MinimumCavernInteriorHeightBlocks,
        long MaximumReliefAboveSeaBlocks,
        long RockFloorTopBlocks,
        long DeepestOceanFloorBlocks,
        long MaximumCavernCeilingBlocks,
        long HighestReliefBlocks,
        bool StrictlyMonotone,
        bool QuantizedNonDecreasing);

    private sealed record FixtureSample(L03BPureLandscapeView View, int Seed, StableId SiteId);

    private sealed record BlindFixtureMetric(
        string Code,
        int SidePixels,
        double SpanBlocks,
        double NominalStepBlocks,
        int TransitionPixelCount,
        double Mean,
        double Variance,
        double MeanAbsoluteSlope,
        double RugosityLag1,
        double RugosityLag4,
        double RugosityLag12,
        double ResidualPlanEnergy,
        int EightNeighborExtremaAtContrastFraction,
        DirectionalJumpStatistics Jumps,
        double DirectionalBalance,
        int SaturatedPixelCount);

    private sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record FixtureCorpus(IReadOnlyList<int> CalibrationSeeds, IReadOnlyList<int> HoldoutSeeds)
    {
        public static FixtureCorpus Load(byte[] bytes)
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            return new FixtureCorpus(
                document.RootElement.GetProperty("calibration_seeds").EnumerateArray().Select(item => item.GetInt32()).ToArray(),
                document.RootElement.GetProperty("holdout_seeds").EnumerateArray().Select(item => item.GetInt32()).ToArray());
        }
    }

    private sealed record JumpStatistics(double Maximum, double P95, double P95ToMaximumRatio);

    private sealed record DirectionalJumpStatistics(JumpStatistics Horizontal, JumpStatistics Vertical, JumpStatistics MainDiagonal, JumpStatistics AntiDiagonal)
    {
        public bool IsFinite => new[] { Horizontal, Vertical, MainDiagonal, AntiDiagonal }
            .All(item => double.IsFinite(item.Maximum) && double.IsFinite(item.P95) && double.IsFinite(item.P95ToMaximumRatio) && item.P95ToMaximumRatio is >= 0 and <= 1);
    }

    private sealed record CorpusMetric(
        string Corpus,
        int Seed,
        string ProfileId,
        string GeographyConfigHash,
        string PlateSnapshotChecksum,
        string LandscapeChecksum,
        Dictionary<string, int> CellFamilyCounts,
        double MinimumModelAltitude,
        double MaximumModelAltitude,
        double MinimumAltitudeBlocks,
        double MaximumAltitudeBlocks,
        double MaximumBathymetryBlocks,
        long DeepestOceanFloorBlocks,
        long HighestReliefBlocks,
        IReadOnlyList<SeedFamilyMorphology> FamilyMorphology);

    private sealed record SeedFamilyMorphology(
        string Family,
        int CellCount,
        string Status,
        PureViewGeometry? View,
        L03BMorphologyMeasurement? Measurement,
        IReadOnlyList<ExpectedTrait> Traits);

    private sealed record PureViewGeometry(
        string OwnerCellId,
        long CenterX,
        long CenterZ,
        int SidePixels,
        double SpanBlocks,
        double NominalStepBlocks,
        double MacroSpanFactor,
        int TransitionPixelCount);

    private sealed record ExpectedTrait(
        string Name,
        double Observed,
        string Comparison,
        double Expected,
        bool Passed);

    private sealed record TraitFailureRate(
        string Corpus,
        string Family,
        string Trait,
        int Attempts,
        int Failures,
        int Passes,
        double FailureRate,
        IReadOnlyList<int> FailedSeeds);
}
