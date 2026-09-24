using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

var json=new JsonSerializerOptions{WriteIndented=true,IncludeFields=true};
if(args.Length==0){Console.WriteLine(JsonSerializer.Serialize(ReliefChecks.Run(),json));return 0;}
if(args.Length is not(3 or 5))throw new ArgumentException("Usage: NEW_OUTPUT SEED control|coupled [WIDTH_BLOCKS LENGTH_BLOCKS]");
string root=Path.GetFullPath(args[0]),mode=args[2];
int seed=int.Parse(args[1],CultureInfo.InvariantCulture);
if(mode is not("control" or "coupled"))throw new ArgumentException("Unknown complete-generation mode");
long width=args.Length==5?long.Parse(args[3],CultureInfo.InvariantCulture):1000000;
long length=args.Length==5?long.Parse(args[4],CultureInfo.InvariantCulture):1000000;
var scale=new TectonicScalePlan(width,length);
if(Directory.Exists(root)||File.Exists(root))throw new IOException("Never overwrite prior evidence.");
Directory.CreateDirectory(root);
string commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"UNCOMMITTED";
Write("INCOMPLETE.json",new{seed,mode,commit});
try
{
    const int side=512;
    var settings=new TectonicEvolutionSettings(side:side,duration:36);
    var initial=TectonicHistory.Generate(seed,scale,new(side:side,duration:0));
    var assembly=ContinentalAssemblage.Generate(seed,scale,side,CrustTransport.Sum(initial.Initial.ContinentalKm)/(side*side));
    var options=mode=="coupled"?new LithostaticReliefOptions():new LithostaticReliefOptions(0,false);
    var world=LithostaticReliefWorld.Generate(seed,scale,settings,assembly,options);
    var result=world.MechanicalHistory;var h=result.History;
    var fields=new Dictionary<string,object>();
    Save("initial-height",world.InitialElevationKm.Select(v=>168+12*v).ToArray(),"solid Y blocks");
    Save("height",world.ElevationKm.Select(v=>168+12*v).ToArray(),"solid Y blocks");
    Save("elevation-model",world.ElevationKm,"signed km model relative to datum; no water mask");
    Save("legacy-height",h.Final.ElevationKm.Select(v=>168+12*v).ToArray(),"solid Y from previous local response on THIS material state");
    Save("thermal-only-height",Enumerable.Range(0,side*side).Select(i=>168+12*LithostaticRelief.Column(
        h.Final.ContinentalKm[i],h.Final.OceanicKm[i],h.Final.OceanAge[i],true).ElevationKm).ToArray(),"finite plate response on THIS material state");
    Save("pressure-potential",world.PotentialKm2,"depth-integrated potential / rho_m/g, km2");
    Save("continental-thickness",h.Final.ContinentalKm,"equivalent km");
    Save("oceanic-thickness",h.Final.OceanicKm,"equivalent km");
    Save("ocean-age",h.Final.OceanAge,"transported mean Myr model; inherited age prior is explicit");
    Save("inherited-ocean",h.Final.InheritedOceanicKm,"equivalent km of initial ocean still present");
    Save("plastic-strain",Enumerable.Range(0,side*side).Select(result.Memory.Strain).ToArray(),"dimensionless accumulated plastic excess");
    foreach(long size in new[]{131072L,262144L,1000000L})
        if(width==length&&Math.Abs(world.SampleElevationKm(new(size,size),size*.375,size*.625)-world.SampleElevationKm(scale,width*.375,length*.625))>1e-10)
            throw new ArithmeticException("Atlas was cropped when resizing");
    Write("ledger.json",h.Ledger);Write("solves.json",result.Solves);
    Write("manifest.json",new{commit,seed,mode,algorithm=LithostaticRelief.AlgorithmId,settings,options,
        width=side,height=side,worldWidthBlocks=width,worldLengthBlocks=length,mechanicalSide=128,
        referenceWidth=scale.ReferenceWidth,referenceLength=scale.ReferenceLength,
        referenceKmPerUnit=MaterialBoundHistory.ReferenceKmPerUnit,sampleStepBlocksX=width/(double)side,sampleStepBlocksZ=length/(double)side,
        h.InitialMaterialChecksum,h.FinalMaterialChecksum,world.Checksum,fields,waterSurfacePresent=false,seabedMasked=false,
        pressureResponse="ACTUAL_NEGATIVE_GPE_GRADIENT_IN_EVERY_MECHANICAL_SOLVE_NOT_POSTPROCESS",
        inheritedOceanAge="UNIFORM_50_MYR_PRIOR_REMAINS_EXPLICIT_NOT_RECONSTRUCTED_PREHISTORY",
        oceanSource="EXISTING_CONSERVATIVE_EXCHANGE_NO_NEW_FRACTURE_TOPOLOGY",
        oceanThermal="FINITE_COOLING_AT_TRANSPORTED_MEAN_AGE_NOT_FULL_COHORT_THERMAL_DISTRIBUTION",
        geographicAcceptance="NOT_ACCEPTED",erosion="NOT_RUN",physicalCalibration="REDUCED_MODEL_PRIORS_NOT_EARTH_CALIBRATED",
        initialOrigins=h.InitialContinentalByOrigin,finalOrigins=h.FinalContinentalByOrigin,
        maximumForceResidual=result.Solves.Max(s=>s.RelativeResidual),runtime=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription});
    Write("COMPLETE.json",new{commit,seed,mode,status="EXECUTED_WORLD_NOT_GEOGRAPHIC_PASS",count=fields.Count});
    File.Delete(Path.Combine(root,"INCOMPLETE.json"));
    Console.WriteLine($"WORLD COMPLETE {seed} {mode} Y=[{world.ElevationKm.Min()*12+168:R},{world.ElevationKm.Max()*12+168:R}]");
    return 0;
    void Save(string name,IReadOnlyList<double> data,string unit)
    {
        if(data.Count!=side*side||data.Any(v=>!double.IsFinite(v)))throw new ArithmeticException("Invalid field "+name);
        var bytes=new byte[data.Count*8];for(int i=0;i<data.Count;i++)BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i*8,8),data[i]);
        using(var stream=new FileStream(Path.Combine(root,name+".f64le"),FileMode.CreateNew))stream.Write(bytes);
        fields.Add(name,new{path=name+".f64le",sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),unit});
    }
}
catch(Exception ex){Write("FAILED.json",new{commit,seed,mode,error=ex.ToString()});Console.Error.WriteLine(ex);return 1;}
void Write(string name,object data){using var file=new FileStream(Path.Combine(root,name),FileMode.CreateNew);JsonSerializer.Serialize(file,data,json);}
