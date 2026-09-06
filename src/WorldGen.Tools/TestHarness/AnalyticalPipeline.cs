using System.Collections.Concurrent;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tools.TestHarness;

internal sealed record AnalyticalFixture(string Name, int Width, int Length, Func<int, int, double> Surface);

internal static class AnalyticalFixtureCatalog
{
    private static readonly IReadOnlyDictionary<string, AnalyticalFixture> Fixtures =
        new Dictionary<string, AnalyticalFixture>(StringComparer.Ordinal)
        {
            ["plane-x"] = new("plane-x", 9, 9, static (x, _) => 80d - (2d * x)),
            ["saddle"] = new(
                "saddle",
                9,
                9,
                static (x, z) => 90d + ((x - 4d) * (x - 4d)) - ((z - 4d) * (z - 4d))),
        };

    internal static bool Contains(string name) => Fixtures.ContainsKey(name);

    internal static AnalyticalFixture Get(string name) => Fixtures[name];
}

internal sealed class AnalyticalSnapshotBundle
{
    internal AnalyticalSnapshotBundle(HeightSnapshot height, VoxelSnapshot voxels)
    {
        Height = height;
        Voxels = voxels;
    }

    internal HeightSnapshot Height { get; }

    internal VoxelSnapshot Voxels { get; }
}

internal static class AnalyticalPipeline
{
    private const int WorldHeight = 384;

    internal static GenerationResult<AnalyticalSnapshotBundle> Run(RunCommand command)
    {
        GenerationResult<AnalyticalSnapshotBundle>? boundaryFailure = CancellationAt(command, "validate-input");
        if (boundaryFailure is not null)
        {
            return boundaryFailure;
        }

        boundaryFailure = CancellationAt(command, "prepare-fixture");
        if (boundaryFailure is not null)
        {
            return boundaryFailure;
        }

        AnalyticalFixture fixture = AnalyticalFixtureCatalog.Get(command.Fixture);
        long requiredWork = checked((long)fixture.Width * fixture.Length * 2L);

        boundaryFailure = CancellationAt(command, "generate-data");
        if (boundaryFailure is not null)
        {
            return boundaryFailure;
        }

        if (command.Budget < requiredWork)
        {
            return Failure(
                command,
                GenerationFailureCode.BudgetExceeded,
                "generate-data",
                $"Budget {command.Budget} is below the required {requiredWork} deterministic work units.",
                true);
        }

        IReadOnlyList<HeightSampleInput> inputs = GenerateHeightInputs(command, fixture);

        boundaryFailure = CancellationAt(command, "validate-snapshot");
        if (boundaryFailure is not null)
        {
            return boundaryFailure;
        }

        try
        {
            GenerationIdentity identity = CreateIdentity(command, fixture);
            var dimensions = new SnapshotDimensions(fixture.Width, fixture.Length, WorldHeight);
            var units = new SnapshotUnits("block", "block/256");
            HeightSnapshot height = HeightSnapshot.Create(
                identity,
                "analytical-height",
                1,
                [],
                dimensions,
                units,
                inputs);
            byte[] heightBytes = HeightSnapshotBinaryCodec.Serialize(height);
            Hash256 heightHash = Hash256.Compute(heightBytes);
            VoxelSnapshot voxels = VoxelSnapshot.Create(
                identity,
                "synthetic-core-voxels",
                1,
                [heightHash],
                dimensions,
                units,
                height.Samples.Select(sample => new VoxelCell(
                    StableId.Derive(RandomDomain.Sites, sample.Id, 0),
                    sample.X,
                    checked((int)(sample.QuantizedHeight / 256)),
                    sample.Z,
                    VoxelMaterial.Rock)));
            var candidate = new AnalyticalSnapshotBundle(height, voxels);

            boundaryFailure = CancellationAt(command, "publish-snapshot");
            if (boundaryFailure is not null)
            {
                return boundaryFailure;
            }

            return GenerationResult<AnalyticalSnapshotBundle>.Success(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException)
        {
            return Failure(
                command,
                GenerationFailureCode.InvalidInput,
                "validate-snapshot",
                exception.Message,
                false);
        }
    }

    private static IReadOnlyList<HeightSampleInput> GenerateHeightInputs(
        RunCommand command,
        AnalyticalFixture fixture)
    {
        int cellCount = checked(fixture.Width * fixture.Length);
        int[] indices = Enumerable.Range(0, cellCount).ToArray();
        if (command.Order == "reverse")
        {
            Array.Reverse(indices);
        }
        else if (command.Order == "permuted")
        {
            Array.Sort(indices, (left, right) => PermutationKey(command.Seed, left).CompareTo(PermutationKey(command.Seed, right)));
        }

        var preparedCache = new Dictionary<int, HeightSampleInput>();
        if (command.Cache == "hot")
        {
            foreach (int index in Enumerable.Range(0, cellCount))
            {
                preparedCache[index] = CreateSample(command, fixture, index);
            }
        }

        var samples = new ConcurrentBag<HeightSampleInput>();
        Parallel.ForEach(
            indices,
            new ParallelOptions { MaxDegreeOfParallelism = command.Workers },
            index => samples.Add(command.Cache == "hot"
                ? preparedCache[index]
                : CreateSample(command, fixture, index)));

        HeightSampleInput[] result = samples.ToArray();
        if (command.Fault == "nonfinite")
        {
            int index = Array.FindIndex(result, sample => sample.X == 0 && sample.Z == 0);
            result[index] = result[index] with { HeightBlocks = double.NaN };
        }

        return result;
    }

    private static HeightSampleInput CreateSample(RunCommand command, AnalyticalFixture fixture, int index)
    {
        int x = index % fixture.Width;
        int z = index / fixture.Width;
        StableId id = StableId.Derive(RandomDomain.Geology, StableId.Zero, checked((ulong)index));
        ulong noise = StatelessRandomV1.NextUInt64(command.Seed, RandomDomain.Geology, id, 0);
        double jitter = ((long)(noise & 1023UL) - 512L) / 256d;
        return new HeightSampleInput(id, x, z, fixture.Surface(x, z) + jitter);
    }

    private static ulong PermutationKey(int seed, int index)
    {
        StableId id = StableId.Derive(RandomDomain.Sites, StableId.Zero, checked((ulong)index));
        return StatelessRandomV1.NextUInt64(seed, RandomDomain.Sites, id, 0);
    }

    private static GenerationIdentity CreateIdentity(RunCommand command, AnalyticalFixture fixture)
    {
        byte[] fixtureDefinition = Encoding.UTF8.GetBytes(
            fixture.Name == "plane-x"
                ? "fixture:v1;size=9x9;surface=80-2*x;jitter=geology-v1/256"
                : "fixture:v1;size=9x9;surface=90+(x-4)^2-(z-4)^2;jitter=geology-v1/256");
        return new GenerationIdentity(
            command.Seed,
            GenerationIdentity.SupportedAlgorithmVersion,
            GenerationIdentity.SupportedSchemaVersion,
            command.ConfigHash,
            Hash256.Compute(fixtureDefinition),
            "isrworldgen-core-harness-v1");
    }

    private static GenerationResult<AnalyticalSnapshotBundle>? CancellationAt(RunCommand command, string stage) =>
        command.Fault == $"cancel:{stage}"
            ? Failure(
                command,
                GenerationFailureCode.Cancelled,
                stage,
                $"Injected cancellation at the '{stage}' boundary.",
                true)
            : null;

    private static GenerationResult<AnalyticalSnapshotBundle> Failure(
        RunCommand command,
        GenerationFailureCode code,
        string stage,
        string details,
        bool canRetry) =>
        GenerationResult<AnalyticalSnapshotBundle>.Failure(
            new GenerationError(
                code,
                command.Seed,
                stage,
                StableId.Zero,
                command.ConfigHash,
                details,
                canRetry));
}
