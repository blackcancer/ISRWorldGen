using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Contracts;

public abstract class SnapshotContractException : Exception
{
    protected SnapshotContractException(string message)
        : base(message)
    {
    }

    protected SnapshotContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class UnsupportedSnapshotVersionException : SnapshotContractException
{
    public UnsupportedSnapshotVersionException(uint version)
        : base($"Snapshot schema version {version} is not supported.") => Version = version;

    public uint Version { get; }
}

public sealed class UnsupportedAlgorithmVersionException : SnapshotContractException
{
    public UnsupportedAlgorithmVersionException(uint version)
        : base($"Generation algorithm version {version} is not supported.") => Version = version;

    public uint Version { get; }
}

public sealed class CorruptSnapshotException : SnapshotContractException
{
    public CorruptSnapshotException(string message)
        : base(message)
    {
    }

    public CorruptSnapshotException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class DuplicateStableIdException : SnapshotContractException
{
    public DuplicateStableIdException(StableId id)
        : base($"Duplicate StableId {id} is forbidden in a canonical snapshot.") => Id = id;

    public StableId Id { get; }
}

public sealed class DuplicateParentHashException : SnapshotContractException
{
    public DuplicateParentHashException(Hash256 hash)
        : base($"Duplicate parent hash {hash} is forbidden in a snapshot header.") => Hash = hash;

    public Hash256 Hash { get; }
}
