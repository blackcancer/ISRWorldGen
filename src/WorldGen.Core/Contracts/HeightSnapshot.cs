using System.Collections.ObjectModel;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Contracts;

public readonly record struct HeightSampleInput(StableId Id, long X, long Z, double HeightBlocks);

public readonly record struct HeightSample(StableId Id, long X, long Z, long QuantizedHeight);

/// <summary>Immutable, canonically sorted synthetic height snapshot.</summary>
public sealed class HeightSnapshot
{
    private HeightSnapshot(SnapshotHeader header, HeightSample[] samples)
    {
        Header = header;
        Samples = Array.AsReadOnly(samples);
    }

    public SnapshotHeader Header { get; }

    public ReadOnlyCollection<HeightSample> Samples { get; }

    public static HeightSnapshot Create(
        GenerationIdentity identity,
        string stage,
        ulong revision,
        IEnumerable<Hash256> parentHashes,
        SnapshotDimensions dimensions,
        SnapshotUnits units,
        IEnumerable<HeightSampleInput> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        HeightSample[] quantized = samples
            .Select(sample => new HeightSample(
                sample.Id,
                sample.X,
                sample.Z,
                HeightQuantizer.QuantizeBlocks(sample.HeightBlocks)))
            .ToArray();

        return CreateQuantized(identity, stage, revision, parentHashes, dimensions, units, quantized);
    }

    internal static HeightSnapshot CreateQuantized(
        GenerationIdentity identity,
        string stage,
        ulong revision,
        IEnumerable<Hash256> parentHashes,
        SnapshotDimensions dimensions,
        SnapshotUnits units,
        IEnumerable<HeightSample> samples)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parentHashes);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(samples);

        HeightSample[] canonicalSamples = samples.ToArray();
        Array.Sort(canonicalSamples, static (left, right) => CompareStableIds(left.Id, right.Id));
        ValidateSamples(canonicalSamples, dimensions);

        Hash256 checksum = HeightSnapshotBinaryCodec.ComputeContentChecksum(canonicalSamples);
        var header = new SnapshotHeader(
            identity,
            stage,
            revision,
            parentHashes,
            dimensions,
            units,
            checksum);
        return new HeightSnapshot(header, canonicalSamples);
    }

    internal static int CompareStableIds(StableId left, StableId right)
    {
        int comparison = left.High.CompareTo(right.High);
        return comparison != 0 ? comparison : left.Low.CompareTo(right.Low);
    }

    private static void ValidateSamples(IReadOnlyList<HeightSample> samples, SnapshotDimensions dimensions)
    {
        long maximumHeight = checked((long)dimensions.Height * HeightQuantizer.UnitsPerBlock);

        for (int index = 0; index < samples.Count; index++)
        {
            HeightSample sample = samples[index];
            if (sample.X < 0 || sample.X >= dimensions.Width || sample.Z < 0 || sample.Z >= dimensions.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(samples),
                    sample,
                    "Sample coordinates must lie inside the semi-open snapshot dimensions.");
            }

            if (sample.QuantizedHeight < 0 || sample.QuantizedHeight >= maximumHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(samples),
                    sample,
                    "Quantized height must lie inside the semi-open vertical dimension.");
            }

            if (index > 0 && sample.Id == samples[index - 1].Id)
            {
                throw new DuplicateStableIdException(sample.Id);
            }
        }
    }
}
