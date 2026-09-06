namespace ISRWorldGen.Core.Contracts;

/// <summary>
/// Height boundary quantization v1: 1/256 block, IEEE finite input, midpoint-to-even rounding.
/// </summary>
public static class HeightQuantizer
{
    public const int UnitsPerBlock = 256;

    public static long QuantizeBlocks(double blocks)
    {
        if (!double.IsFinite(blocks))
        {
            throw new ArgumentOutOfRangeException(nameof(blocks), blocks, "Height must be finite.");
        }

        double scaled = blocks * UnitsPerBlock;
        if (!double.IsFinite(scaled))
        {
            throw new OverflowException("The quantized height exceeds the Int64 range.");
        }

        return checked((long)Math.Round(scaled, MidpointRounding.ToEven));
    }

    public static double DequantizeBlocks(long quantizedHeight) =>
        quantizedHeight / (double)UnitsPerBlock;
}
