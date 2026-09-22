using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

internal static class QualificationChecks
{
    public static void Run(Action<string, Action> check, string? output)
    {
        SpreadingPhase old = new(7, -100, -40, 0, 0, 0, 1000, 0, 0, 10, 0);
        SpreadingPhase passive = new(-1, -40, 0, 0, 0, 0, 1000, 0, 0, 20, 0, false);
        var timeline = new SpreadingTimeline("extinct", [old, passive]);
        check("extinct ridge birth includes all later displacement", () =>
        {
            Require(timeline.TryResolveBirth(1000, 200, out var w), "Missing old floor");
            Near(w.BirthTimeMyr, -60); Near(w.ReconstructedX, 1000);
            Require(Math.Abs(w.BirthTimeMyr + 1000d / 20) > 1, "Current distance/rate wrongly used");
        });
        check("phase order is canonical rather than caller dependent", () =>
        {
            var t = new SpreadingTimeline("extinct", [passive, old]);
            Require(t.TryResolveBirth(1000, 200, out var w), "Missing floor"); Near(w.BirthTimeMyr, -60);
        });
        check("timeline includes later tangential transport", () =>
        {
            var t = new SpreadingTimeline("sheared", [old, passive with { MaterialVelocityZ = 3 }]);
            Require(t.TryResolveBirth(1000, 320, out var w), "Missing sheared floor");
            Near(w.BirthTimeMyr, -60); Near(w.AlongSegment, .2);
        });
        check("timeline common-frame translation preserves birth", () =>
        {
            const double ux = 7, uz = -3;
            var shifted = timeline.Phases.Select(p => p with {
                Ax = p.Ax + ux * p.StartTimeMyr, Az = p.Az + uz * p.StartTimeMyr,
                Bx = p.Bx + ux * p.StartTimeMyr, Bz = p.Bz + uz * p.StartTimeMyr,
                RidgeVelocityX = p.RidgeVelocityX + ux, RidgeVelocityZ = p.RidgeVelocityZ + uz,
                MaterialVelocityX = p.MaterialVelocityX + ux, MaterialVelocityZ = p.MaterialVelocityZ + uz }).ToArray();
            Require(new SpreadingTimeline("frame", shifted).TryResolveBirth(1000, 200, out var w), "Frame changed coverage"); Near(w.BirthTimeMyr, -60);
        });
        check("gaps overlaps and omitted recent motion are refused", () =>
        {
            Refuse(() => new SpreadingTimeline("gap", [old, passive with { StartTimeMyr = -39 }]));
            Refuse(() => new SpreadingTimeline("overlap", [old, passive with { StartTimeMyr = -41 }]));
            Refuse(() => new SpreadingTimeline("unfinished", [old]));
        });
        check("extinction endpoint remains dated without a newer birth event", () =>
        {
            Require(timeline.TryResolveBirth(800, 200, out var w), "Missing extinction endpoint");
            Near(w.BirthTimeMyr, -40); Require(w.SourceEventId == 7, "Invented recent event");
        });
        check("dormant interval cannot manufacture ocean", () =>
        {
            Require(!timeline.TryResolveBirth(200, 200, out _), "Created material after extinction");
            Refuse(() => new SpreadingTimeline("false-event", [old, passive with { SourceEventId = 8 }]));
        });
        check("timeline retains immutable inputs", () =>
        {
            var input = new[] { old, passive }; var t = new SpreadingTimeline("immutable", input);
            input[0] = old with { MaterialVelocityX = 100 };
            Require(t.TryResolveBirth(1000, 200, out var w), "External mutation changed coverage"); Near(w.BirthTimeMyr, -60);
        });
        var scale = new TectonicScalePlan(1_000_000, 1_000_000);
        SpreadingTimeline Branch(int sign) => new("branch-" + sign, [
            new(10, -175, -25, 500000, 0, 500000, 1000000, 0, 0, sign * 2500, 0),
            new(11, -25, 0, 500000, 0, 500000, 1000000, 0, 0, sign * 5000, 0)]);
        var left = Branch(-1); var right = Branch(1);
        check("shared phase boundary belongs to the younger event", () =>
        {
            Require(right.TryResolveBirth(625000, 500000, out var w), "Unresolved phase boundary");
            Near(w.BirthTimeMyr, -25); Require(w.SourceEventId == 11, "Temporal boundary selected old event");
        });
        check("changing spreading rate does not overwrite inherited dates", () =>
        {
            Require(right.TryResolveBirth(750000, 500000, out var w), "Missing older phase"); Near(w.BirthTimeMyr, -75);
        });
        check("full million-unit analytic basin has complete dated coverage at 512 square", () =>
        {
            const int n = 512; var ocean = Enumerable.Repeat(7d, n*n).ToArray();
            var map = SpreadingTimeline.Reconstruct(73, scale, n, ocean, [right, left], "CONTROLLED_TWO_PHASE_BASIN_NOT_PROCEDURAL_WORLD");
            var reversed = SpreadingTimeline.Reconstruct(73, scale, n, ocean, [left, right], "CONTROLLED_TWO_PHASE_BASIN_NOT_PROCEDURAL_WORLD");
            Require(map.Checksum == reversed.Checksum, "Branch enumeration changed identity");
            var expected = new double[n*n]; var heights = new double[n*n]; var cooling = new double[n*n];
            for (int i = 0; i < expected.Length; i++)
            {
                double distance = Math.Abs((i % n + .5) * 1000000 / n - 500000);
                expected[i] = distance <= 125000 ? distance / 5000 : 25 + (distance - 125000) / 2500;
                Near(map.AgeMyr[i], expected[i], 1e-10);
                heights[i] = 168 + 12 * CrustResponse.ElevationKm(0, 7, map.AgeMyr[i]);
                cooling[i] = OceanCoolingColumn.Evaluate(map.AgeMyr[i]).WaterLoadedSubsidenceMetres;
            }
            if (output is not null)
            {
                string d = Path.Combine(output, "controlled-basin"); Directory.CreateDirectory(d);
                var fields = new Dictionary<string, object>();
                Save("age", map.AgeMyr); Save("height", heights); Save("thermal-subsidence-metres", cooling);
                File.WriteAllText(Path.Combine(d, "manifest.json"), JsonSerializer.Serialize(new {
                    scope = "CONTROLLED_TWO_PHASE_OCEAN_BASIN_NOT_PROCEDURAL_WORLD", seed=73, width=n, height=n,
                    referenceWidth=1000000, referenceLength=1000000, step=1000000d/n,
                    map.Checksum, fields, phases = new[] { left.Phases, right.Phases },
                    heightMeaning="Y=168+12*legacyCrustResponseKm; thermal-subsidence is separate and NOT added", waterSurfacePresent=false,
                    seabedMasked=false, geographicAcceptance="NOT_EVALUATED", erosion="NOT_RUN"
                }, Json));
                void Save(string name, IReadOnlyList<double> values)
                {
                    var bytes = new byte[values.Count*8];
                    for(int i=0;i<values.Count;i++) BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i*8,8),values[i]);
                    string path=name+".f64le";File.WriteAllBytes(Path.Combine(d,path),bytes);
                    fields[name]=new { path, sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
                }
            }
        });
        check("partial and conflicting atlas timelines cannot become complete maps", () =>
        {
            var ocean = Enumerable.Repeat(7d, 32*32).ToArray();
            Refuse(() => SpreadingTimeline.Reconstruct(73,scale,32,ocean,[right],"PARTIAL"));
            var other = new SpreadingTimeline("other", right.Phases.Select(p => p with { SourceEventId = p.SourceEventId+10 }).ToArray());
            Refuse(() => SpreadingTimeline.Reconstruct(73,scale,32,ocean,[right,left,other],"CONFLICT"));
        });
        foreach (int seed in new[] { -437287116, 20260906, 73 })
        check("moving full-atlas uniform-control regression seed " + seed, () =>
        {
            // These are real evolving material worlds, not a new geological prior.
            // The explicit uniform map must reproduce the legacy evolution exactly.
            var settings = new TectonicEvolutionSettings(side:128,duration:36);
            var a = ContinentalAssemblage.Generate(seed,scale,128);
            var ocean = a.OceanicKm.ToArray();
            var births = ocean.Select(v => v>0 ? -50d : 0d).ToArray();
            var map = OceanBirthMap.Create(seed,scale,128,ocean,births,ocean.Select(v=>v>0?0:-1).ToArray(),
                [new OceanFormationEvent(0,-50,-50,"LEGACY_UNIFORM_CONTROL")],"REGRESSION_NOT_NEW_PREHISTORY");
            var legacy = MaterialBoundHistory.GenerateWithAssemblage(seed,scale,settings,a);
            var current = MaterialBoundHistory.GenerateWithOceanBirthMap(seed,scale,settings,a,map);
            Require(legacy.Initial.Checksum == current.Initial.Checksum, "Changed initial state");
            Require(legacy.Final.Checksum == current.Final.Checksum, "Changed evolved material/height state");
            foreach(long size in new long[]{131072,262144,1000000})
                Near(current.SampleElevationKm(new TectonicScalePlan(size,size),size*.37,size*.61),current.SampleElevationKm(scale,370000,610000),1e-10);
            if(output is not null) File.WriteAllText(Path.Combine(output,"regression-"+seed+".json"),JsonSerializer.Serialize(new {
                seed, side=128, duration=36, legacy.Initial.Checksum, finalChecksum=legacy.Final.Checksum,
                maximumDifference=0d, status="PASS", newGeographyGenerated=false
            },Json));
        });
    }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Near(double a,double b,double tolerance=1e-9){Require(double.IsFinite(a)&&Math.Abs(a-b)<=tolerance,$"{a:R} != {b:R}");}
    private static void Refuse(Action action){try{action();}catch(ArgumentException){return;}throw new InvalidOperationException("Expected explicit refusal");}
}
