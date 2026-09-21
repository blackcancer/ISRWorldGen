using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Frozen material state. Arrays never escape as mutable references.</summary>
public sealed class MaterialBoundSnapshot
{
    public int Side { get; }
    public double Time { get; }
    public ReadOnlyCollection<double> ContinentalKm { get; }
    public ReadOnlyCollection<double> OceanicKm { get; }
    public ReadOnlyCollection<double> OceanAge { get; }
    public ReadOnlyCollection<double> InheritedOceanicKm { get; }
    public ReadOnlyCollection<double> ElevationKm { get; }
    public ReadOnlyCollection<double> AccumulatedCompression { get; }
    public ReadOnlyCollection<double> AccumulatedExtension { get; }
    public ReadOnlyCollection<double> AccumulatedShear { get; }
    public ReadOnlyCollection<int> PlateIds { get; }
    public string Checksum { get; }

    internal MaterialBoundSnapshot(int side, double time, double[] continental, double[] oceanic,
        double[] ageMoment, double[] inherited, double[] compression, double[] extension, double[] shear, int[] plateIds)
    {
        Side = side; Time = time;
        ContinentalKm = Array.AsReadOnly((double[])continental.Clone()); OceanicKm = Array.AsReadOnly((double[])oceanic.Clone());
        InheritedOceanicKm = Array.AsReadOnly((double[])inherited.Clone());
        AccumulatedCompression = Array.AsReadOnly((double[])compression.Clone()); AccumulatedExtension = Array.AsReadOnly((double[])extension.Clone());
        AccumulatedShear = Array.AsReadOnly((double[])shear.Clone()); PlateIds = Array.AsReadOnly((int[])plateIds.Clone());
        double[] age = new double[continental.Length], heights = new double[continental.Length];
        for (int i = 0; i < age.Length; i++)
        {
            age[i] = oceanic[i] > 0 ? ageMoment[i] / oceanic[i] : 0;
            heights[i] = CrustResponse.ElevationKm(continental[i], oceanic[i], age[i]);
        }
        OceanAge = Array.AsReadOnly(age); ElevationKm = Array.AsReadOnly(heights);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, time); hash.AppendData(buffer);
        foreach (var field in new[] { ContinentalKm, OceanicKm, OceanAge, InheritedOceanicKm, ElevationKm,
            AccumulatedCompression, AccumulatedExtension, AccumulatedShear })
            foreach (double value in field) { BinaryPrimitives.WriteDoubleLittleEndian(buffer, value); hash.AppendData(buffer); }
        foreach (int value in plateIds) { BinaryPrimitives.WriteInt64LittleEndian(buffer, value); hash.AppendData(buffer); }
        Checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
