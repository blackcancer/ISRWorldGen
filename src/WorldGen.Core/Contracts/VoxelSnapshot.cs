using System.Collections.ObjectModel;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Contracts;

public enum VoxelMaterial : byte
{
    Air = 0,
    Rock = 1,
    Soil = 2,
    Water = 3,
}

public readonly record struct VoxelCell(
    StableId Id,
    long X,
    int Y,
    long Z,
    VoxelMaterial Material);

/// <summary>Immutable synthetic Core voxel payload, independent of Vintage Story block APIs.</summary>
public sealed class VoxelSnapshot
{
    private VoxelSnapshot(SnapshotHeader header, VoxelCell[] voxels)
    {
        Header = header;
        Voxels = Array.AsReadOnly(voxels);
    }

    public SnapshotHeader Header { get; }

    public ReadOnlyCollection<VoxelCell> Voxels { get; }

    public static VoxelSnapshot Create(
        GenerationIdentity identity,
        string stage,
        ulong revision,
        IEnumerable<Hash256> parentHashes,
        SnapshotDimensions dimensions,
        SnapshotUnits units,
        IEnumerable<VoxelCell> voxels)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parentHashes);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(voxels);

        VoxelCell[] canonicalVoxels = voxels.ToArray();
        Array.Sort(canonicalVoxels, static (left, right) => HeightSnapshot.CompareStableIds(left.Id, right.Id));
        Validate(canonicalVoxels, dimensions);
        Hash256 checksum = VoxelSnapshotBinaryCodec.ComputeContentChecksum(canonicalVoxels);
        var header = new SnapshotHeader(
            identity,
            stage,
            revision,
            parentHashes,
            dimensions,
            units,
            checksum);
        return new VoxelSnapshot(header, canonicalVoxels);
    }

    private static void Validate(IReadOnlyList<VoxelCell> voxels, SnapshotDimensions dimensions)
    {
        for (int index = 0; index < voxels.Count; index++)
        {
            VoxelCell voxel = voxels[index];
            if (voxel.X < 0 || voxel.X >= dimensions.Width || voxel.Z < 0 || voxel.Z >= dimensions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(voxels), voxel, "Voxel X/Z lies outside semi-open dimensions.");
            }

            if (voxel.Y < 0 || voxel.Y >= dimensions.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(voxels), voxel, "Voxel Y lies outside the semi-open height.");
            }

            if (!Enum.IsDefined(voxel.Material))
            {
                throw new ArgumentOutOfRangeException(nameof(voxels), voxel, "Voxel material code is unknown.");
            }

            if (index > 0 && voxel.Id == voxels[index - 1].Id)
            {
                throw new DuplicateStableIdException(voxel.Id);
            }
        }
    }
}
