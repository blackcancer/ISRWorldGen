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

internal sealed record SeedHash(int Seed, string CanonicalHash);

internal static class DeterminismFixture
{
    internal static readonly int[] QuickSeeds =
    [
        0, 1, -1, int.MinValue, int.MaxValue, 42, 73, 20_260_906,
        -437_287_116, -1_587_986_303, 1_571_057_779, -1_837_489_763,
        720_496_888, 1_481_968_978, -307_755_313, 60_159_686,
    ];

    internal static int NWorkerCount => Math.Max(2, Environment.ProcessorCount);

    internal static IReadOnlyList<SeedHash> VerifyFullMatrix()
    {
        int[] workerCounts = [1, 2, NWorkerCount];
        EvaluationOrder[] orders = Enum.GetValues<EvaluationOrder>();
        var results = new List<SeedHash>(QuickSeeds.Length);

        foreach (int seed in QuickSeeds)
        {
            string? expectedHash = null;

            foreach (int workerCount in workerCounts)
            {
                foreach (EvaluationOrder order in orders)
                {
                    var coldCache = new ConcurrentDictionary<int, double>();
                    string coldHash = GenerateHash(seed, workerCount, order, coldCache);

                    var hotCache = new ConcurrentDictionary<int, double>();
                    _ = GenerateHash(seed, workerCount, order, hotCache);
                    string hotHash = GenerateHash(seed, workerCount, order, hotCache);

                    expectedHash ??= coldHash;
                    Assert.AreEqual(expectedHash, coldHash, $"cold seed={seed}, workers={workerCount}, order={order}");
                    Assert.AreEqual(expectedHash, hotHash, $"hot seed={seed}, workers={workerCount}, order={order}");
                }
            }

            results.Add(new SeedHash(seed, expectedHash!));
        }

        return results;
    }

    internal static void WriteProcessReportIfRequested(IReadOnlyList<SeedHash> hashes)
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
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            nWorkerCount = NWorkerCount,
            seeds = hashes,
        };

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GenerateHash(
        int seed,
        int workerCount,
        EvaluationOrder order,
        ConcurrentDictionary<int, double> heightCache)
    {
        const int sampleCount = 96;
        StableId root = StableId.Derive(RandomDomain.Geology, StableId.Zero, unchecked((uint)seed));
        int[] indexes = ArrangeIndexes(seed, order, sampleCount);
        var inputs = new ConcurrentBag<HeightSampleInput>();

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
            });

        var identity = new GenerationIdentity(
            seed,
            algorithmVersion: 1,
            schemaVersion: 1,
            Hash256.Compute("l01b-config-v1"u8),
            Hash256.Compute("l01b-assets-v1"u8),
            "net10-x64-l01b-v1");
        HeightSnapshot snapshot = HeightSnapshot.Create(
            identity,
            "l01b.determinism-matrix",
            revision: 1,
            parentHashes: [],
            new SnapshotDimensions(12, 8, 384),
            new SnapshotUnits("block", "block/256"),
            inputs);

        return Hash256.Compute(HeightSnapshotBinaryCodec.Serialize(snapshot)).ToString();
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
