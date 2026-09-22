using ISRWorldGen.Core.Geology.Evolution;

internal static class AtlasChecks
{
    public static readonly TectonicScalePlan Scale = new(1000000, 1000000);

    // Controlled palaeo-ridge hypothesis, independent of coast, present plates
    // and seed. Two conjugate flanks with dated change of spreading speed.
    // Curvature is a declared piecewise-linear geometry, not terrain noise.
    // Total displacement = 100*2000 + 100*3000 = 500000 reference units.
    public static SpreadingTimeline[] Fixture(TectonicScalePlan s, int segments = 64,
        double shiftX = 0, double shiftZ = 0, bool curved = true)
    {
        var result = new List<SpreadingTimeline>();
        for (int k = 0; k < segments; k++)
        {
            double za = s.ReferenceLength * k / segments, zb = s.ReferenceLength * (k + 1) / segments;
            double a = Ridge(k / (double)segments, s.ReferenceWidth, curved) + shiftX;
            double b = Ridge((k + 1d) / segments, s.ReferenceWidth, curved) + shiftX;
            foreach (int sign in new[] { -1, 1 })
                result.Add(new($"segment-{k:D4}-flank-{sign}", new[] {
                    new SpreadingPhase(1, -200, -100, a, za + shiftZ, b, zb + shiftZ,
                        0, 0, sign * s.ReferenceWidth / 500, 0),
                    new SpreadingPhase(2, -100, 0, a, za + shiftZ, b, zb + shiftZ,
                        0, 0, sign * s.ReferenceWidth * 3 / 1000, 0) }));
        }
        return result.ToArray();
    }
    private static double Ridge(double t, double width, bool curved)
    {
        if (!curved) return width / 2;
        double[] offsets = [0, .07, -.04, .09, 0];
        int k = Math.Min((int)(t * 4), 3); double u = t * 4 - k;
        return width * (.5 + offsets[k] + (offsets[k + 1] - offsets[k]) * u * u * (3 - 2 * u));
    }
    private static void Near(double a, double b, double tol = 1e-9)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Abs(a-b) > tol)
            throw new Exception($"Mismatch {a:R} vs {b:R}");
    }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Expected explicit rejection.");
    }

    public static object[] Run()
    {
        var reports = new List<object>();
        void Check(string name, Action action) { action(); reports.Add(new { name, status = "PASS" }); }
        const int n = 32;
        double[] ocean = Enumerable.Repeat(7d, n*n).ToArray();
        var straight = Fixture(Scale, 1, curved:false);
        var r = SpreadingAtlas.Reconstruct(73, Scale, n, ocean, straight, "controlled");
        Check("straight equals original exhaustive inversion", () => {
            var old = SpreadingTimeline.Reconstruct(73, Scale, n, ocean, straight, "controlled");
            for (int i=0;i<n*n;i++) Near(r.Births.AgeMyr[i], old.AgeMyr[i], 0);
        });
        Check("known forward displacement in both velocity phases", () => {
            for (int x=0;x<n;x++) {
                double d=Math.Abs((x+.5)*1000000/n-500000);
                double age=d<=300000 ? d/3000 : 100+(d-300000)/2000;
                Near(r.Births.AgeMyr[x],age);
            }
        });
        var curved = Fixture(Scale);
        var c = SpreadingAtlas.Reconstruct(73,Scale,n,ocean,curved,"curved");
        Check("curved full atlas covered without uniform fallback", () => {
            if(c.Receipt.OceanCells!=n*n || c.Births.AgeMyr.Min()<0 || c.Births.AgeMyr.Max()>200)
                throw new Exception("Coverage or age domain.");
        });
        Check("input order invariance", () => {
            var reversed=SpreadingAtlas.Reconstruct(73,Scale,n,ocean,curved.Reverse().ToArray(),"curved");
            if(reversed.Births.Checksum!=c.Births.Checksum) throw new Exception("Order changed chronology.");
        });
        Check("periodic unwrapped X translation", () => {
            var shifted=SpreadingAtlas.Reconstruct(73,Scale,n,ocean,Fixture(Scale,shiftX:1000000),"shifted");
            for(int i=0;i<n*n;i++) Near(c.Births.AgeMyr[i],shifted.Births.AgeMyr[i]);
        });
        Check("periodic unwrapped Z translation", () => {
            var shifted=SpreadingAtlas.Reconstruct(73,Scale,n,ocean,Fixture(Scale,shiftZ:1000000),"shifted");
            for(int i=0;i<n*n;i++) Near(c.Births.AgeMyr[i],shifted.Births.AgeMyr[i]);
        });
        Check("no implicit wrapping in nonperiodic mode", () =>
            Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,n,ocean,Fixture(Scale,shiftX:1000000),"shifted",false)));
        Check("missing flank rejects uncovered material", () =>
            Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,n,ocean,straight.Take(1).ToArray(),"gap")));
        Check("conflicting ages never averaged", () => {
            var conflict = new SpreadingTimeline("conflict", [new SpreadingPhase(3,-200,0,500000,0,500000,1000000,0,0,5000,0)]);
            Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,n,ocean,straight.Append(conflict).ToArray(),"conflict"));
        });
        Check("duplicate timeline identity rejected", () =>
            Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,n,ocean,straight.Concat(straight).ToArray(),"duplicate")));
        Check("event identity cannot combine incompatible periods", () => {
            var conflict = new SpreadingTimeline("event-conflict",[new SpreadingPhase(1,-200,0,500000,0,500000,1000000,0,0,5000,0)]);
            Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,n,ocean,straight.Append(conflict).ToArray(),"conflict"));
        });
        Check("absent carrier has neither birth nor event", () => {
            double[] mixed=(double[])ocean.Clone(); mixed[3]=0;
            var m=SpreadingAtlas.Reconstruct(73,Scale,n,mixed,curved,"absent");
            if(m.Births.AgeMyr[3]!=0 || m.Births.SourceEventIds[3]!=-1) throw new Exception("Invented material.");
        });
        Check("invalid carriers rejected", () => {
            double[] q=(double[])ocean.Clone(); q[5]=double.NaN;
            Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,n,q,curved,"nan"));
            q[5]=-1; Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,n,q,curved,"negative"));
        });
        Check("ocean amounts preserved", () => {
            if(ocean.Any(v=>v!=7)) throw new Exception("Modified input.");
        });
        Check("whole atlas resizing does not crop", () => {
            foreach(long size in new long[]{131072,262144,1000000}) {
                var m=SpreadingAtlas.Reconstruct(73,new(size,size),n,ocean,curved,"scale");
                for(int i=0;i<n*n;i++) Near(c.Births.AgeMyr[i],m.Births.AgeMyr[i],0);
            }
        });
        Check("rectangular atlas and units preserved", () => {
            var s=new TectonicScalePlan(1000000,500000);
            var m=SpreadingAtlas.Reconstruct(73,s,n,ocean,Fixture(s),"rectangle");
            for(int i=0;i<n*n;i++) Near(c.Births.AgeMyr[i],m.Births.AgeMyr[i],1e-9);
        });
        Check("index removes impossible candidate tests", () => {
            if(c.Receipt.TestedTimelines >= (long)n*n*curved.Length/3) throw new Exception("Index ineffective.");
        });
        Check("endpoint footprint includes dormant later transport", () => {
            var t=new SpreadingTimeline("dormant",[
                new SpreadingPhase(9,-100,-50,0,0,0,1000000,0,0,10000,0),
                new SpreadingPhase(-1,-50,0,0,0,0,1000000,0,0,10000,0,false)]);
            double[] q = new double[n*n]; for(int z=0;z<n;z++)for(int x=n/2;x<n;x++)q[z*n+x]=7;
            var m=SpreadingAtlas.Reconstruct(73,Scale,n,q,[t],"dormant",false);
            for(int x=n/2;x<n;x++) Near(m.Births.AgeMyr[x],(x+.5)*1000000/n/10000);
        });
        Check("empty carrier does not invent chronology", () => {
            var m=SpreadingAtlas.Reconstruct(73,Scale,n,new double[n*n],curved,"empty");
            if(m.Receipt.OceanCells!=0||m.Receipt.TestedTimelines!=0)throw new Exception("Empty world queried.");
        });
        Check("resolution and provenance validation", () => {
            Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,31,ocean,curved,"wrong"));
            Throws(()=>SpreadingAtlas.Reconstruct(73,Scale,n,ocean,curved,""));
        });
        return reports.ToArray();
    }
}
