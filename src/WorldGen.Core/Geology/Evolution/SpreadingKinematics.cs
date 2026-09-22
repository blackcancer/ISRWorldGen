namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// One straight spreading segment, translated at constant velocity during a
/// declared time interval. Ax/Bx etc. are the extrapolated segment at t=0.
/// A parcel born at t<0 subsequently follows MaterialVelocity. All horizontal
/// quantities use the SAME reference units; time is model Myr. This is an exact
/// controlled kinematic episode, not an inferred history of an arbitrary atlas.
/// Segment endpoints are not wrapped implicitly; unwrap periodic geometry first.
/// </summary>
public sealed record SpreadingEpisode(int SourceEventId, double Ax, double Az, double Bx, double Bz,
    double RidgeVelocityX, double RidgeVelocityZ, double MaterialVelocityX, double MaterialVelocityZ,
    double FirstBirthTimeMyr, double LastBirthTimeMyr);

public readonly record struct OceanBirthWitness(int SourceEventId, double BirthTimeMyr,
    double AlongSegment, double ReconstructedX, double ReconstructedZ);

public static class SpreadingKinematics
{
    public const string AlgorithmId = "piecewise-translating-ridge-birth-inversion-v1";

    /// <summary>
    /// x_now = A_0 + s*(B_0-A_0) + (V_material - V_ridge)*age.
    /// Solve for s and age; accept ONLY inside the swept segment and the active
    /// birth interval. No shortest-distance replacement, extrapolation beyond
    /// the event, or reassignment to a current nearest ridge is allowed.
    /// </summary>
    public static bool TryResolveBirth(SpreadingEpisode episode, double x, double z, out OceanBirthWitness witness)
    {
        ArgumentNullException.ThrowIfNull(episode); witness = default;
        if (episode.SourceEventId < 0 || new[] { episode.Ax, episode.Az, episode.Bx, episode.Bz,
            episode.RidgeVelocityX, episode.RidgeVelocityZ, episode.MaterialVelocityX, episode.MaterialVelocityZ,
            episode.FirstBirthTimeMyr, episode.LastBirthTimeMyr, x, z }.Any(v => !double.IsFinite(v))
            || episode.FirstBirthTimeMyr > episode.LastBirthTimeMyr || episode.LastBirthTimeMyr > 0)
            throw new ArgumentException("Invalid spreading geometry, interval or provenance.");
        double sx = episode.Bx - episode.Ax, sz = episode.Bz - episode.Az;
        double vx = episode.MaterialVelocityX - episode.RidgeVelocityX;
        double vz = episode.MaterialVelocityZ - episode.RidgeVelocityZ;
        double length = Math.Sqrt(sx * sx + sz * sz), speed = Math.Sqrt(vx * vx + vz * vz);
        double determinant = sx * vz - sz * vx;
        if (!double.IsFinite(length) || !double.IsFinite(speed) || !double.IsFinite(determinant)
            || length <= 0 || speed <= 0 || Math.Abs(determinant / length / speed) < 1e-12)
            throw new ArgumentException("Degenerate/tangential episode cannot create a swept oceanic area.");
        double rx = x - episode.Ax, rz = z - episode.Az;
        double s = (rx * vz - rz * vx) / determinant;
        double age = (sx * rz - sz * rx) / determinant;
        if (!double.IsFinite(s) || !double.IsFinite(age)) throw new ArithmeticException("Birth inversion overflow.");
        if (s < 0 || s > 1 || -age < episode.FirstBirthTimeMyr || -age > episode.LastBirthTimeMyr) return false;
        double forwardX = episode.Ax + s * sx + vx * age;
        double forwardZ = episode.Az + s * sz + vz * age;
        double scale = Math.Max(1, Math.Max(Math.Max(Math.Abs(x), Math.Abs(z)), length + speed * age));
        if (!double.IsFinite(scale) || !double.IsFinite(forwardX) || !double.IsFinite(forwardZ)
            || Math.Abs(forwardX - x) > 1e-10 * scale || Math.Abs(forwardZ - z) > 1e-10 * scale)
            throw new ArithmeticException("Birth witness does not reconstruct the observed parcel.");
        witness = new(episode.SourceEventId, -age, s, forwardX, forwardZ);
        return true;
    }
}
