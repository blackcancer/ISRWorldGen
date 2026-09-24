using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

/// <summary>Lossless PNG of measured samples, no resampling or water surface.
/// Fixed Y0..383 for all seeds. Invalid/unrepresentable heights are rejected.</summary>
internal static class NumericHeightPng
{
    internal static byte[] Encode(IReadOnlyList<double> values, int side, bool preview = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (side < 1 || side > 4096 || values.Count != side * side ||
            values.Any(v => !double.IsFinite(v) || v < 0 || v > 383))
            throw new ArgumentException("Solid height outside the fixed encoding; no clipping.");
        int bytesPerPixel = preview ? 1 : 2;
        var scanlines = new byte[side * (1 + bytesPerPixel * side)];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int k = z * (1 + bytesPerPixel * side) + 1 + x * bytesPerPixel;
            double scaled = values[z * side + x] * (preview ? 255d : 65535d) / 383;
            if (preview) scanlines[k] = (byte)Math.Round(scaled, MidpointRounding.ToEven);
            else BinaryPrimitives.WriteUInt16BigEndian(scanlines.AsSpan(k, 2), (ushort)Math.Round(scaled, MidpointRounding.ToEven));
        }
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true)) deflate.Write(scanlines);
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), side);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), side);
        header[8] = (byte)(preview ? 8 : 16);
        Chunk("IHDR", header);
        Chunk("tEXt", Encoding.ASCII.GetBytes("Description\0Solid Y=code*383/" + (preview ? "255; PREVIEW ONLY" : "65535; NUMERIC HEIGHTMAP") + "; includes seabed; no resampling"));
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
        return output.ToArray();
        void Chunk(string type, byte[] payload)
        {
            byte[] size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, payload.Length); output.Write(size);
            byte[] tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(payload);
            uint crc = 0xffffffff;
            foreach (byte b in tag.Concat(payload))
            {
                crc ^= b;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(size, ~crc); output.Write(size);
        }
    }
}
