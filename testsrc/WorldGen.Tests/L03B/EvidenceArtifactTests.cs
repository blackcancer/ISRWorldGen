using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Plates;

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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly (string Code, LandscapeFamily Family)[] BlindOrder =
    [
        ("R01", LandscapeFamily.OldMassifs),
        ("R02", LandscapeFamily.Plateaus),
        ("R03", LandscapeFamily.VolcanicDomains),
        ("R04", LandscapeFamily.RuggedRanges),
        ("R05", LandscapeFamily.SedimentaryBasins),
        ("R06", LandscapeFamily.Plains),
    ];

    [TestMethod]
    [DoNotParallelize]
    public void T0305AndT0306PublishAtomicBlindReviewEvidence()
    {
        string runName = Environment.GetEnvironmentVariable("ISR_L03B_EVIDENCE_RUN") ?? "evidence-q";
        if (runName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || runName.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Evidence run name must be a single safe directory name.");
        }
        string output = Path.Combine(L03BTestSupport.FindRepositoryRoot(), ".local", "L03B", runName);
        Directory.CreateDirectory(output);
        string reportPath = Path.Combine(output, "T03-05-06-R.json");
        string commit = Environment.GetEnvironmentVariable("ISR_L03B_EVIDENCE_COMMIT") ?? "WORKING_TREE";
        string configuration = Environment.GetEnvironmentVariable("ISR_L03B_EVIDENCE_CONFIGURATION") ?? "UNKNOWN";
        WriteAtomic(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            requirementIds = new[] { "R03-05", "R03-06" },
            automatedStatus = "RUNNING",
            qualitativeReviewStatus = "REVIEW_REQUIRED",
            overallStatus = "RUNNING",
            commit,
            configuration,
        }, JsonOptions));

        try
        {
            RunCampaign(output, reportPath, commit, configuration);
        }
        catch (Exception exception)
        {
            WriteAtomic(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                requirementIds = new[] { "R03-05", "R03-06" },
                automatedStatus = "FAIL",
                qualitativeReviewStatus = "NOT_RUN",
                overallStatus = "FAIL",
                commit,
                configuration,
                failure = new { type = exception.GetType().FullName, exception.Message },
            }, JsonOptions));
            throw;
        }
    }

    private static void RunCampaign(string output, string reportPath, string commit, string configuration)
    {
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
        var mapRecords = new List<object>();
        var profileRows = new StringBuilder("view,index,normalized-height\n");
        var sampledByCode = new Dictionary<string, double[,]>(StringComparer.Ordinal);
        foreach ((string code, LandscapeFamily family) in BlindOrder)
        {
            LandscapeFamilyProfile profile = LandscapeFamilyCatalog.Get(family);
            double[,] samples = SampleFixture(family, profile, 96, 96);
            sampledByCode.Add(code, samples);
            BlindFixtureMetric metric = Measure(code, profile, samples);
            fixtureMetrics.Add(metric);
            Assert.IsGreaterThan(MinimumTargetVariance, metric.Variance, code);

            byte[] bitmap = RenderBitmap(samples);
            string mapPath = Path.Combine(output, $"T03-06-{code}.bmp");
            WriteAtomic(mapPath, bitmap);
            mapRecords.Add(new
            {
                code,
                file = Path.GetFileName(mapPath),
                sha256 = L03BTestSupport.Sha256(bitmap),
            });

            int middle = samples.GetLength(0) / 2;
            for (int x = 0; x < samples.GetLength(1); x++)
            {
                profileRows.Append(code).Append(',').Append(x).Append(',')
                    .Append(samples[middle, x].ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        for (int left = 0; left < BlindOrder.Length; left++)
        {
            for (int right = left + 1; right < BlindOrder.Length; right++)
            {
                Assert.IsGreaterThan(
                    MinimumPairwiseMeanAbsoluteDifference,
                    MeanAbsoluteDifference(
                        sampledByCode[BlindOrder[left].Code],
                        sampledByCode[BlindOrder[right].Code]),
                    $"{BlindOrder[left].Code}/{BlindOrder[right].Code}");
            }
        }

        byte[] profileBytes = Encoding.UTF8.GetBytes(profileRows.ToString());
        string profilesPath = Path.Combine(output, "T03-06-R-blind-profiles.csv");
        WriteAtomic(profilesPath, profileBytes);
        byte[] keyBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            instruction = "Inspect R01..R06 maps and profiles before opening this separate key.",
            entries = BlindOrder.Select(item => new { item.Code, family = item.Family.ToString() }),
        }, JsonOptions);
        string keyPath = Path.Combine(output, "T03-06-R-review-key.json");
        WriteAtomic(keyPath, keyBytes);

        IReadOnlyList<int> calibrationSeeds = L03BTestSupport.SeedCorpus("calibration_seeds");
        IReadOnlyList<int> holdoutSeeds = L03BTestSupport.SeedCorpus("holdout_seeds");
        Assert.HasCount(192, calibrationSeeds);
        Assert.HasCount(64, holdoutSeeds);
        FrozenScaleProfile balanced = L03BTestSupport.FrozenProfile("balanced");
        FrozenScaleProfile vast = L03BTestSupport.FrozenProfile("vast-expeditions");
        string progressPath = Path.Combine(output, "T03-06-Q-progress.json");
        var corpusEntries = new List<CorpusMetric>(256);
        foreach ((string corpusName, IReadOnlyList<int> seeds, FrozenScaleProfile corpusProfile) in
                 new[] { ("balanced", calibrationSeeds, balanced), ("vast", holdoutSeeds, vast) })
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
        int corpusFamilyCount = corpus.SelectMany(item => item.CellFamilyCounts.Keys).Distinct().Count();
        Assert.IsGreaterThanOrEqualTo(MinimumCorpusFamilies, corpusFamilyCount);
        Assert.IsTrue(corpus.All(item => item.MinimumModelAltitude is >= -1 and <= 1));
        Assert.IsTrue(corpus.All(item => item.MaximumModelAltitude is >= -1 and <= 1));
        Assert.IsTrue(corpus.All(item => item.MinimumAltitudeBlocks >= item.DeepestOceanFloorBlocks));
        Assert.IsTrue(corpus.All(item => item.MaximumAltitudeBlocks <= item.HighestReliefBlocks));
        Assert.IsTrue(corpus.Any(item => item.MaximumBathymetryBlocks > 0));

        string fixturesPath = Path.Combine(L03BTestSupport.FindRepositoryRoot(), "registry", "fixtures.json");
        object report = new
        {
            schemaVersion = 1,
            requirementIds = new[] { "R03-05", "R03-06" },
            automatedStatus = "PASS",
            qualitativeReviewStatus = "REVIEW_REQUIRED",
            overallStatus = "REVIEW_REQUIRED",
            reason = "T03-06 requires independent recognition review; numeric thresholds are a predeclared non-normative alarm only.",
            commit,
            configuration,
            targetFramework = "net10.0",
            frozenFixtureCorpus = new
            {
                calibrationSeeds,
                holdoutSeeds,
                fixturesSha256 = L03BTestSupport.Sha256(File.ReadAllBytes(fixturesPath)),
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
                    maps = mapRecords,
                    profiles = new
                    {
                        file = Path.GetFileName(profilesPath),
                        sha256 = L03BTestSupport.Sha256(profileBytes),
                    },
                    separateKey = new
                    {
                        file = Path.GetFileName(keyPath),
                        sha256 = L03BTestSupport.Sha256(keyBytes),
                        instruction = "Reviewer must inspect neutral-code artifacts before opening the key.",
                    },
                    fixtureMetrics,
                },
                measures = "mean, variance, mean absolute slope, roughness at lags 1/4/12, central profiles",
                unfilteredCorpus = corpus,
                corpusFamilyCount,
            },
        };

        // PASS is the last atomic write, after every assertion and every referenced artifact.
        WriteAtomic(reportPath, JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions));
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
            new LandscapeGenerationSettings(budget, profile.SiteQuota, 4)));
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
            model.VerticalPlan.HighestReliefBlocks);
    }

    private static void WriteCampaignProgress(string path, string corpus, int seed, string stage) =>
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(new { corpus, seed, stage }, JsonOptions));

    private static double[,] SampleFixture(LandscapeFamily family, LandscapeFamilyProfile profile, int width, int height)
    {
        // Targeted fixtures may choose their owning cell, but maps always traverse the published composition
        // (site blending, family amplitude and vertical-budget input), never a raw signature sampler.
        int[] fixtureSeeds = [20260907, -20260907, 731, -731, 196883, -196883, 48731, -48731];
        FrozenScaleProfile frozen = L03BTestSupport.FrozenProfile("vast-expeditions");
        foreach (int seed in fixtureSeeds)
        {
            GenerationIdentity identity = L03BTestSupport.Identity(seed, frozen);
            (AtlasMesh atlas, PlateAtlasSnapshot plates) = L03BTestSupport.PlateFixture(seed, frozen);
            LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates, frozen,
                new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), frozen.SiteQuota, 4)));
            LandscapeCellProfile? cell = model.Cells.FirstOrDefault(item => item.Family == family);
            if (cell is null || cell.Value.CellId == default)
            {
                continue;
            }
            AtlasSite site = atlas.Sites.Single(item => item.Id == cell.Value.CellId);
            var samples = new double[height, width];
            double span = profile.MacroWavelengthBlocks * 1.7;
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    long sampleX = Math.Clamp((long)Math.Round(site.X + (((x / (double)(width - 1)) - .5) * span)), atlas.Bounds.MinX, atlas.Bounds.MaxXExclusive - 1);
                    long sampleZ = Math.Clamp((long)Math.Round(site.Z + (((z / (double)(height - 1)) - .5) * span)), atlas.Bounds.MinZ, atlas.Bounds.MaxZExclusive - 1);
                    samples[z, x] = model.Sample(sampleX, sampleZ).ModelAltitudeNormalized;
                }
            }
            return samples;
        }
        throw new AssertFailedException(
            $"No declared targeted fixture contains family {family}.");
    }

    private static BlindFixtureMetric Measure(
        string code,
        LandscapeFamilyProfile profile,
        double[,] samples)
    {
        double[] values = samples.Cast<double>().ToArray();
        double mean = values.Average();
        double variance = values.Select(value => (value - mean) * (value - mean)).Average();
        return new BlindFixtureMetric(
            code,
            profile.ParameterChecksum.ToString(),
            mean,
            variance,
            Roughness(samples, 1),
            Roughness(samples, 4),
            Roughness(samples, 12));
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

    private static byte[] RenderBitmap(double[,] samples)
    {
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
        string temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
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

    private sealed record BlindFixtureMetric(
        string Code,
        string ParameterChecksum,
        double Mean,
        double Variance,
        double MeanAbsoluteSlope,
        double RugosityLag4,
        double RugosityLag12);

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
        long HighestReliefBlocks);
}
