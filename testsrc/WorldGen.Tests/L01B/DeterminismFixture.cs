using System.Collections.Concurrent;
using System.Text.Json;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L01B;

internal enum EvaluationOrder
{
    Linear,
    Reverse,
    DeterministicallyPermuted,
}

internal sealed record SeedHashes(int Seed, string CanonicalDataHash, string CanonicalVoxelHash);

internal sealed record SnapshotArtifacts(byte[] DataBytes, byte[] VoxelBytes)
{
    internal string DataHash => Hash256.Compute(DataBytes).ToString();

    internal string VoxelHash => Hash256.Compute(VoxelBytes).ToString();
}

internal static class DeterminismFixture
{
    internal static readonly int[] QuickSeeds =
    [
        0, 1, -1, int.MinValue, int.MaxValue, 42, 73, 20_260_906,
        -437_287_116, -1_587_986_303, 1_571_057_779, -1_837_489_763,
        720_496_888, 1_481_968_978, -307_755_313, 60_159_686,
    ];

    internal static int NWorkerCount => Math.Max(2, Environment.ProcessorCount);

    internal const string DataStage = "l01b.determinism-matrix";
    internal const string VoxelStage = "l01b.determinism-voxels";

    internal static Hash256 GeographyConfigHash => Hash256.Compute("l01b-config-v1"u8);

    internal static Hash256 GenerationAssetHash => Hash256.Compute("l01b-assets-v1"u8);

    internal static IReadOnlyList<SeedHashes> VerifyFullMatrix()
    {
        int[] workerCounts = [1, 2, NWorkerCount];
        EvaluationOrder[] orders = Enum.GetValues<EvaluationOrder>();
        var results = new List<SeedHashes>(QuickSeeds.Length);

        foreach (int seed in QuickSeeds)
        {
            SnapshotArtifacts? expected = null;

            foreach (int workerCount in workerCounts)
            {
                foreach (EvaluationOrder order in orders)
                {
                    var coldCache = new ConcurrentDictionary<int, double>();
                    SnapshotArtifacts cold = GenerateArtifacts(seed, workerCount, order, coldCache);

                    var hotCache = new ConcurrentDictionary<int, double>();
                    _ = GenerateArtifacts(seed, workerCount, order, hotCache);
                    SnapshotArtifacts hot = GenerateArtifacts(seed, workerCount, order, hotCache);

                    expected ??= cold;
                    CollectionAssert.AreEqual(expected.DataBytes, cold.DataBytes, $"data bytes cold seed={seed}, workers={workerCount}, order={order}");
                    CollectionAssert.AreEqual(expected.DataBytes, hot.DataBytes, $"data bytes hot seed={seed}, workers={workerCount}, order={order}");
                    CollectionAssert.AreEqual(expected.VoxelBytes, cold.VoxelBytes, $"voxel bytes cold seed={seed}, workers={workerCount}, order={order}");
                    CollectionAssert.AreEqual(expected.VoxelBytes, hot.VoxelBytes, $"voxel bytes hot seed={seed}, workers={workerCount}, order={order}");
                    Assert.AreEqual(expected.DataHash, cold.DataHash, $"data hash cold seed={seed}, workers={workerCount}, order={order}");
                    Assert.AreEqual(expected.DataHash, hot.DataHash, $"data hash hot seed={seed}, workers={workerCount}, order={order}");
                    Assert.AreEqual(expected.VoxelHash, cold.VoxelHash, $"voxel hash cold seed={seed}, workers={workerCount}, order={order}");
                    Assert.AreEqual(expected.VoxelHash, hot.VoxelHash, $"voxel hash hot seed={seed}, workers={workerCount}, order={order}");
                }
            }

            results.Add(new SeedHashes(seed, expected!.DataHash, expected.VoxelHash));
        }

        return results;
    }

    internal static void WriteProcessReportIfRequested(IReadOnlyList<SeedHashes> hashes)
    {
        string? outputPath = Environment.GetEnvironmentVariable("ISRW_L01B_PROCESS_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var report = new
        {
            schemaVersion = 1,
            processId = Environment.ProcessId,
            observedUtc = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            commit = Environment.GetEnvironmentVariable("ISRW_L01B_COMMIT") ?? "unreported",
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            nWorkerCount = NWorkerCount,
            heightEnvelopeVersion = HeightSnapshotBinaryCodec.EnvelopeVersion,
            voxelEnvelopeVersion = VoxelSnapshotBinaryCodec.EnvelopeVersion,
            generationSchemaVersion = GenerationIdentity.SupportedSchemaVersion,
            generationAlgorithmVersion = GenerationIdentity.SupportedAlgorithmVersion,
            geographyConfigHash = GeographyConfigHash.ToString(),
            generationAssetHash = GenerationAssetHash.ToString(),
            dataStage = DataStage,
            voxelStage = VoxelStage,
            seeds = hashes,
        };

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static SnapshotArtifacts GenerateArtifacts(
        int seed,
        int workerCount,
        EvaluationOrder order,
        ConcurrentDictionary<int, double> heightCache)
    {
        const int sampleCount = 96;
        StableId root = StableId.Derive(RandomDomain.Geology, StableId.Zero, unchecked((uint)seed));
        int[] indexes = ArrangeIndexes(seed, order, sampleCount);
        var inputs = new ConcurrentBag<HeightSampleInput>();
        var voxels = new ConcurrentBag<VoxelCell>();

        Parallel.ForEach(
            indexes,
            new ParallelOptions { MaxDegreeOfParallelism = workerCount },
            index =>
            {
                double height = heightCache.GetOrAdd(index, value => ComputeHeight(seed, root, value));
                inputs.Add(new HeightSampleInput(
                    StableId.Derive(RandomDomain.Hydrology, root, unchecked((ulong)index)),
                    X: index % 12,
                    Z: index / 12,
                    HeightBlocks: height));
                int surfaceY = checked((int)Math.Floor(height));
                for (int layer = 0; layer < 3; layer++)
                {
                    int y = surfaceY - 2 + layer;
                    VoxelMaterial material = layer switch
                    {
                        0 => VoxelMaterial.Rock,
                        1 => VoxelMaterial.Soil,
                        _ when surfaceY < 110 => VoxelMaterial.Water,
                        _ => VoxelMaterial.Air,
                    };
                    voxels.Add(new VoxelCell(
                        StableId.Derive(RandomDomain.Caverns, root, checked(((ulong)index * 3) + (ulong)layer)),
                        X: index % 12,
                        Y: y,
                        Z: index / 12,
                        Material: material));
                }
            });

        var identity = new GenerationIdentity(
            seed,
            algorithmVersion: 1,
            schemaVersion: 1,
            GeographyConfigHash,
            GenerationAssetHash,
            "net10-x64-l01b-v1");
        HeightSnapshot snapshot = HeightSnapshot.Create(
            identity,
            DataStage,
            revision: 1,
            parentHashes: [],
            new SnapshotDimensions(12, 8, 384),
            new SnapshotUnits("block", "block/256"),
            inputs);
        VoxelSnapshot voxelSnapshot = VoxelSnapshot.Create(
            identity,
            VoxelStage,
            revision: 1,
            parentHashes: [],
            new SnapshotDimensions(12, 8, 384),
            new SnapshotUnits("block", "block"),
            voxels);

        return new SnapshotArtifacts(
            HeightSnapshotBinaryCodec.Serialize(snapshot),
            VoxelSnapshotBinaryCodec.Serialize(voxelSnapshot));
    }

    private static int[] ArrangeIndexes(int seed, EvaluationOrder order, int count)
    {
        IEnumerable<int> indexes = Enumerable.Range(0, count);
        return order switch
        {
            EvaluationOrder.Linear => indexes.ToArray(),
            EvaluationOrder.Reverse => indexes.Reverse().ToArray(),
            EvaluationOrder.DeterministicallyPermuted => indexes
                .OrderBy(index => StatelessRandomV1.NextUInt64(
                    seed,
                    RandomDomain.Sites,
                    StableId.Zero,
                    unchecked((ulong)index)))
                .ThenBy(index => index)
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
        };
    }

    private static double ComputeHeight(int seed, StableId root, int index)
    {
        ulong bits = StatelessRandomV1.NextUInt64(
            seed,
            RandomDomain.Geology,
            root,
            unchecked((ulong)index));
        long centered = unchecked((long)(bits & 0x1fffffUL)) - 0x100000L;
        return 160.0 + (centered / 8192.0);
    }
}
