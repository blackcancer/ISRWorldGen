namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Connects actual ribbon-breakup events to the existing piecewise
/// spreading inversion. It does not infer rifts from coastlines or draw a world.</summary>
public static class RiftSpreadingAdapter
{
    public static SpreadingTimeline[] ToTimelines(RiftNecking rift, string identity,
        double originX, double originZ, double normalX, double normalZ)
    {
        ArgumentNullException.ThrowIfNull(rift);
        double alongLengthReference = rift.AlongRiftLengthReference;
        if (string.IsNullOrWhiteSpace(identity) || new[] { originX, originZ, normalX, normalZ, alongLengthReference }
            .Any(v => !double.IsFinite(v)) || alongLengthReference <= 0
            || Math.Abs(normalX * normalX + normalZ * normalZ - 1) > 1e-12)
            throw new ArgumentException("Use a finite unwrapped, orthonormal rift frame.");
        var sources = rift.SpreadingPhases();
        if (!sources.Any(p => p.CreatesOcean)) return [];
        var left = new List<SpreadingPhase>(); var right = new List<SpreadingPhase>();
        foreach (var p in sources)
        {
            double ax = originX + normalX * p.RidgeAtStartReference;
            double az = originZ + normalZ * p.RidgeAtStartReference;
            // Tangent = (-nz,nx). The ridge segment is shared by BOTH flanks.
            double bx = ax - normalZ * alongLengthReference, bz = az + normalX * alongLengthReference;
            left.Add(Make(p.LeftMaterialVelocity)); right.Add(Make(p.RightMaterialVelocity));
            SpreadingPhase Make(double speed) => new(p.EventId,
                p.StartMyr - rift.ObservationTimeMyr, p.EndMyr - rift.ObservationTimeMyr,
                ax, az, bx, bz, p.RidgeVelocity * normalX, p.RidgeVelocity * normalZ,
                speed * normalX, speed * normalZ, p.CreatesOcean);
        }
        return [new(identity + "/rift-" + rift.Checksum + "/left", left), new(identity + "/rift-" + rift.Checksum + "/right", right)];
    }
}
