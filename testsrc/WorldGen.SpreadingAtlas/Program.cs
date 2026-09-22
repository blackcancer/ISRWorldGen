using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

if(args.Length is < 1 or > 2) throw new ArgumentException("Usage: NEW_OUTPUT_DIRECTORY [SEED]");
string root=Path.GetFullPath(args[0]);
if(Directory.Exists(root)||File.Exists(root))throw new IOException("Evidence path exists; never overwrite a campaign.");
Directory.CreateDirectory(root);
var json=new JsonSerializerOptions{WriteIndented=true};
string commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"UNCOMMITTED";
Write("INCOMPLETE.json",new{commit,status="INCOMPLETE"});
try {
    var checks=AtlasChecks.Run(); Write("checks.json",new{commit,count=checks.Length,checks,status="PASS"});
    Console.WriteLine($"PASS {checks.Length} indexed chronology checks");
    if(args.Length==2) {
        int seed=int.Parse(args[1],CultureInfo.InvariantCulture);
        if(seed is not (-437287116 or 20260906 or 73))throw new ArgumentException("Use fixed seeds.");
        const int side=512;
        var scale=AtlasChecks.Scale;
        var settings=new TectonicEvolutionSettings(side:side,duration:0);
        var legacy=TectonicHistory.Generate(seed,scale,settings);
        var assemblage=ContinentalAssemblage.Generate(seed,scale,side,CrustTransport.Sum(legacy.Initial.ContinentalKm)/(side*side));
        // Same prescribed palaeo-ridge for all seeds: deliberate causal control.
        // NOT a ridge inferred from present plates, nor an automated planet.
        var timelines=AtlasChecks.Fixture(scale);
        var resolved=SpreadingAtlas.Reconstruct(seed,scale,side,assemblage.OceanicKm,timelines,
            "CONTROLLED_CURVED_PALEORIDGE_PRIOR_NOT_RECONSTRUCTED_EARTH_OR_AUTOMATIC_GEOLOGY");
        Write("palaeo-events.json",timelines.Select(t=>new{t.Name,t.Phases}));
        Write("reconstruction.json",new{resolved.Receipt,resolved.Births.Checksum,resolved.Births.CarrierChecksum,
            scope="CONTROLLED_PRIOR_EXPERIMENT_NOT_ACCEPTED_GENERATOR",samePriorForAllSeeds=true});
        var current=MaterialBoundHistory.BeginPrehistory(seed,scale,settings,assemblage,resolved.Births);
        Export(current,"stage-000");
        current=current.ContinuePrehistory(scale,36);
        Export(current,"stage-036");
        Write("COMPLETE.json",new{commit,seed,status="EXACT_PRESCRIBED_HISTORY_EXPERIMENT",checks=checks.Length,
            scope="CONTROLLED_PRIOR_NOT_AUTOMATIC_TECTONICS",geographicAcceptance="NOT_EVALUATED",erosion="NOT_RUN"});
        void Export(MaterialBoundHistory history,string stage) {
            var f=history.Final;Directory.CreateDirectory(Path.Combine(root,stage));
            var fields=new Dictionary<string,object>();
            Save("height",f.ElevationKm.Select(v=>168+12*v).ToArray(),"solid Y (blocks)");
            Save("elevation-model",f.ElevationKm,"signed km model relative to marine datum");
            Save("continental-thickness",f.ContinentalKm,"equivalent km continental crust");
            Save("oceanic-thickness",f.OceanicKm,"equivalent km oceanic crust");
            Save("ocean-age",f.OceanAge,"model Myr; prescribed prior, transported mean");
            Save("inherited-oceanic-thickness",f.InheritedOceanicKm,"equivalent km initial ocean carrier");
            if(f.Time==0) {
                Save("birth-time",resolved.Births.BirthTimeMyr,"model Myr relative to observation t=0");
                Save("birth-event",resolved.Births.SourceEventIds.Select(v=>(double)v).ToArray(),"event ID; -1 absent carrier");
            }
            foreach(long size in new long[]{131072,262144,1000000}) {
                double a=history.SampleElevationKm(new(size,size),.375*size,.625*size);
                double b=history.SampleElevationKm(scale,375000,625000);
                if(Math.Abs(a-b)>1e-10)throw new Exception("Atlas resize changed/cropped history.");
            }
            Write(stage+"/ledger.json",history.Ledger);
            Write(stage+"/summary.json",new{history.Checksum,history.InitialAssemblageChecksum,
                history.InitialMaterialChecksum,history.FinalMaterialChecksum,coverage=OceanPrehistory.Describe(history),
                minimumKm=f.ElevationKm.Min(),maximumKm=f.ElevationKm.Max()});
            Write(stage+"/manifest.json",new{
                scope="CONTROLLED_CURVED_PALEORIDGE_PRIOR_ON_EXISTING_ASSEMBLAGE_NOT_AUTOMATIC_WORLD",
                seed,commit,width=side,height=side,worldWidthBlocks=1000000,worldLengthBlocks=1000000,
                referenceWidth=scale.ReferenceWidth,referenceLength=scale.ReferenceLength,
                referenceKmPerUnit=MaterialBoundHistory.ReferenceKmPerUnit,blocksPerModelKm=12,seaLevelReferenceBlocks=168,
                boundary="PERIODIC_PLANAR_PREPARATORY_ATLAS",timeMyr=f.Time,materialSide=side,mechanicalSide=(int?)null,
                mechanics="PRESCRIBED_MATERIAL_MOTION_NOT_FORCE_SOLVER",settings=history.Settings,
                algorithm=SpreadingAtlas.AlgorithmId,history.InitialOceanBirthMapChecksum,
                fields,waterSurfacePresent=false,seabedMasked=false,
                runtime=RuntimeInformation.FrameworkDescription,operatingSystem=RuntimeInformation.OSDescription,
                geographicAcceptance="NOT_EVALUATED",erosion="NOT_RUN",nativeGame="NOT_RUN",
                priorLimitations="One explicitly prescribed curved spreading system; not inferred from generated continents. No post-hoc height painting."
            });
            Write(stage+"/COMPLETE.json",new{status="EXACT_FIELDS_WRITTEN_NOT_GEOGRAPHIC_ACCEPTANCE"});
            Console.WriteLine($"STAGE seed={seed} t={f.Time:R} min={f.ElevationKm.Min():R} max={f.ElevationKm.Max():R}");
            void Save(string name,IReadOnlyList<double> a,string units) {
                if(a.Count!=side*side||a.Any(v=>!double.IsFinite(v)))throw new ArithmeticException("Invalid field.");
                var bytes=new byte[a.Count*8];for(int i=0;i<a.Count;i++)BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i*8,8),a[i]);
                using(var stream=new FileStream(Path.Combine(root,stage,name+".f64le"),FileMode.CreateNew))stream.Write(bytes);
                fields.Add(name,new{path=name+".f64le",sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    units,encoding="float64 little-endian row-major X right Z down",min=a.Min(),max=a.Max()});
            }
        }
    } else Write("COMPLETE.json",new{commit,status="UNIT_CHECKS_ONLY",checks=checks.Length});
    File.Delete(Path.Combine(root,"INCOMPLETE.json"));return 0;
} catch(Exception ex) {Write("FAILED.json",new{commit,status="FAIL",error=ex.ToString()});Console.Error.WriteLine(ex);return 1;}

void Write(string name,object value) {
    using var stream=new FileStream(Path.Combine(root,name),FileMode.CreateNew);
    JsonSerializer.Serialize(stream,value,json);
}
