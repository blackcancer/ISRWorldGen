using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Landscapes;

/// <summary>
/// Geometry-only relief helpers. Distances and noise are evaluated in world space,
/// not at raster indices. None of these operations transports or removes sediment.
/// </summary>
internal static class RawReliefStructure
{
    internal readonly record struct Segment(double Ax, double Az, double Bx, double Bz);

    // Distance to a union, not accumulated influence per segment. Subdividing a
    // straight contact cannot create an extra uplift peak at its new vertex.
    internal static double DistanceSquared(double x, double z, IReadOnlyList<Segment> segments)
    {
        double result = double.PositiveInfinity;
        foreach (Segment segment in segments)
        {
            double dx = segment.Bx - segment.Ax, dz = segment.Bz - segment.Az;
            double length2 = dx * dx + dz * dz;
            if (!(length2 > 0)) throw new ArgumentException("A contact segment must have positive length.");
            double t = Math.Clamp(((x - segment.Ax) * dx + (z - segment.Az) * dz) / length2, 0d, 1d);
            double qx = x - segment.Ax - t * dx, qz = z - segment.Az - t * dz;
            result = Math.Min(result, qx * qx + qz * qz);
        }
        return result;
    }

    internal static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    internal static double Smooth(double a, double b, double v) => Fade(Math.Clamp((v - a) / (b - a), 0d, 1d));

    internal static double Noise(int seed, double x, double z, ulong key)
    {
        long ix = (long)Math.Floor(x), iz = (long)Math.Floor(z);
        double u = Fade(x - ix), v = Fade(z - iz);
        double a = Unit(ix, iz), b = Unit(ix + 1, iz), c = Unit(ix, iz + 1), d = Unit(ix + 1, iz + 1);
        return 2 * ((a + u * (b - a)) * (1 - v) + (c + u * (d - c)) * v) - 1;
        double Unit(long i, long j) => (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology,
            new StableId(unchecked((ulong)i), unchecked((ulong)j)), key) >> 11) / 9007199254740992d;
    }

    internal static double Fractal(int seed, double x, double z, double wavelength, ulong key, int octaves = 4)
    {
        double result = 0, weight = 1, total = 0;
        x /= wavelength; z /= wavelength;
        for (int octave = 0; octave < octaves; octave++)
        {
            result += weight * Noise(seed, x, z, key * 16 + (ulong)octave); total += weight;
            (x, z) = (1.6 * x - 1.2 * z + 19.3, 1.2 * x + 1.6 * z - 7.1); weight *= .5;
        }
        return result / total;
    }

    // Detail is multiplied by its coarser support. Valleys stay valleys instead
    // of receiving the same uniform fine texture as crests and abyssal plains.
    internal static double Mountain(int seed, double x, double z, double scale, ulong key)
    {
        double warpX = Fractal(seed, x, z, scale * 1.3, key + 101, 3);
        double warpZ = Fractal(seed, x, z, scale * 1.3, key + 102, 3);
        x = x / scale + .5 * warpX; z = z / scale + .5 * warpZ;
        double sum = 0, total = 0, amplitude = 1, support = 1;
        for (int octave = 0; octave < 7; octave++)
        {
            double ridge = 1 - Math.Abs(Noise(seed, x, z, key * 16 + (ulong)octave));
            ridge *= ridge;
            double signal = ridge * support;
            sum += amplitude * signal; total += amplitude;
            support = Math.Min(1d, signal * 2.1);
            amplitude *= .53;
            (x, z) = (1.68 * x - 1.26 * z + 3.7, 1.26 * x + 1.68 * z + 13.1);
        }
        return sum / total;
    }
}
