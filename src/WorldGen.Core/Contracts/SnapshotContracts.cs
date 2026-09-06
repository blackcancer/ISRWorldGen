using System.Collections.ObjectModel;
using System.Text;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Contracts;

public sealed record GenerationIdentity
{
    public const uint SupportedSchemaVersion = 1;
    public const uint SupportedAlgorithmVersion = StatelessRandomV1.AlgorithmVersion;

    public GenerationIdentity(
        int nativeSeed,
        uint algorithmVersion,
        uint schemaVersion,
        Hash256 geographyConfigHash,
        Hash256 generationAssetHash,
        string determinismProfileId)
    {
        if (algorithmVersion != SupportedAlgorithmVersion)
        {
            throw new UnsupportedAlgorithmVersionException(algorithmVersion);
        }

        if (schemaVersion != SupportedSchemaVersion)
        {
            throw new UnsupportedSnapshotVersionException(schemaVersion);
        }

        NativeSeed = nativeSeed;
        AlgorithmVersion = algorithmVersion;
        SchemaVersion = schemaVersion;
        GeographyConfigHash = geographyConfigHash;
        GenerationAssetHash = generationAssetHash;
        DeterminismProfileId = CanonicalText.Require(determinismProfileId, nameof(determinismProfileId));
    }

    public int NativeSeed { get; }

    public uint AlgorithmVersion { get; }

    public uint SchemaVersion { get; }

    public Hash256 GeographyConfigHash { get; }

    public Hash256 GenerationAssetHash { get; }

    public string DeterminismProfileId { get; }
}

public sealed record SnapshotDimensions
{
    public SnapshotDimensions(long width, long length, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        }

        _ = checked(checked(width * length) * height);
        Width = width;
        Length = length;
        Height = height;
    }

    public long Width { get; }

    public long Length { get; }

    public int Height { get; }
}

public sealed record SnapshotUnits
{
    public SnapshotUnits(string horizontal, string vertical)
    {
        Horizontal = CanonicalText.Require(horizontal, nameof(horizontal));
        Vertical = CanonicalText.Require(vertical, nameof(vertical));
    }

    public string Horizontal { get; }

    public string Vertical { get; }
}

public sealed class SnapshotHeader
{
    internal SnapshotHeader(
        GenerationIdentity identity,
        string stage,
        ulong revision,
        IEnumerable<Hash256> parentHashes,
        SnapshotDimensions dimensions,
        SnapshotUnits units,
        Hash256 contentChecksum)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parentHashes);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(units);

        Hash256[] parents = parentHashes.ToArray();
        Array.Sort(parents);
        for (int index = 1; index < parents.Length; index++)
        {
            if (parents[index - 1] == parents[index])
            {
                throw new DuplicateParentHashException(parents[index]);
            }
        }

        Identity = identity;
        Stage = CanonicalText.Require(stage, nameof(stage));
        Revision = revision;
        ParentHashes = Array.AsReadOnly(parents);
        Dimensions = dimensions;
        Units = units;
        ContentChecksum = contentChecksum;
    }

    public GenerationIdentity Identity { get; }

    public string Stage { get; }

    public ulong Revision { get; }

    public ReadOnlyCollection<Hash256> ParentHashes { get; }

    public SnapshotDimensions Dimensions { get; }

    public SnapshotUnits Units { get; }

    public Hash256 ContentChecksum { get; }
}

internal static class CanonicalText
{
    internal static string Require(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Canonical text cannot be empty or whitespace.", parameterName);
        }

        if (!value.IsNormalized(NormalizationForm.FormC))
        {
            throw new ArgumentException("Canonical text must already use Unicode normalization form C.", parameterName);
        }

        return value;
    }
}
