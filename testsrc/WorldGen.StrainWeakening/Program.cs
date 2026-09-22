using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

var json=new JsonSerializerOptions{WriteIndented=true};
if(args.Length==0){Console.WriteLine(JsonSerializer.Serialize(WeakeningChecks.Run(),json));return 0;}
if(args.Length!=3)throw new ArgumentException("Usage: NEW_OUTPUT_DIRECTORY SEED control|weakening");
string root=Path.GetFullPath(args[0]),mode=args[2];int seed=int.Parse(args[1],CultureInfo.InvariantCulture);
if(seed is not(-437287116 or 20260906 or 73)||mode is not("control" or "weakening"))throw new ArgumentException("Frozen paired world campaign only.");
if(Directory.Exists(root)||File.Exists(root))throw new IOException("Never overwrite prior evidence.");
Directory.CreateDirectory(root);string commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"UNCOMMITTED";
Write("INCOMPLETE.json",new{seed,mode,commit});
try
{
    const int side=512;var scale=new TectonicScalePlan(1_000_000,1_000_000);var settings=new TectonicEvolutionSettings(side:side,duration:36);
    var old=TectonicHistory.Generate(seed,scale,new(side:side,duration:0));
    var assemblage=ContinentalAssemblage.Generate(seed,scale,side,CrustTransport.Sum(old.Initial.ContinentalKm)/(side*side));
    var options=new StrainWeakeningOptions(residualRatio:mode=="control"?1:.35);
    var result=MaterialBoundHistory.GenerateWithWeakening(seed,scale,settings,assemblage,options);var h=result.History;
    var fields=new Dictionary<string,object>();
    Save("initial-height",h.Initial.ElevationKm.Select(v=>168+12*v).ToArray(),"solid Y blocks");
    Save("height",h.Final.ElevationKm.Select(v=>168+12*v).ToArray(),"solid Y blocks");
    Save("elevation-model",h.Final.ElevationKm,"native signed km model relative to datum");
    Save("continental-thickness",h.Final.ContinentalKm,"equivalent km");
    Save("oceanic-thickness",h.Final.OceanicKm,"equivalent km");
    Save("ocean-age",h.Final.OceanAge,"mean model Myr");
    Save("strain-carrier",result.Memory.Carrier,"equivalent continental km");
    Save("strain-moment",result.Memory.Moment,"continental km times accumulated viscous strain");
    Save("viscous-strain",Enumerable.Range(0,side*side).Select(result.Memory.Strain).ToArray(),"dimensionless, zero where no continent");
    Save("compression",h.Final.AccumulatedCompression,"Eulerian cumulative strain, not material memory");
    foreach(long size in new[]{131072L,262144L,1000000L})
        if(Math.Abs(h.SampleElevationKm(new(size,size),size*.375,size*.625)-h.SampleElevationKm(scale,375000,625000))>1e-10)throw new ArithmeticException("Resizing cropped atlas.");
    Write("solves.json",result.Solves);Write("ledger.json",h.Ledger);
    Write("manifest.json",new{seed,mode,commit,algorithm=StrainWeakeningRheology.AlgorithmId,policy=result.Policy,settings,options,
        width=side,height=side,worldWidthBlocks=1000000,worldLengthBlocks=1000000,mechanicalSide=options.MechanicalSide,
        sampleStepBlocks=1000000d/side,h.InitialMaterialChecksum,h.FinalMaterialChecksum,h.Checksum,h.InitialAssemblageChecksum,
        fields,waterSurfacePresent=false,seabedMasked=false,geographicAcceptance="NOT_ACCEPTED",erosion="NOT_RUN",
        rheology="REDUCED_VISCOUS_STRAIN_SOFTENING_NOT_PLASTICITY_OR_FRACTURE",oceanSources="EXISTING_EXCHANGE_RULE_UNCHANGED_NOT_RIFT_TOPOLOGY",
        initialOceanAge="UNIFORM_PRIOR_UNCHANGED",historyMoment=result.Memory.InitialMoment,producedMoment=result.Memory.ProducedMoment,
        initialOrigins=h.InitialContinentalByOrigin,finalOrigins=h.FinalContinentalByOrigin,
        maximumForceResidual=result.Solves.Max(s=>s.RelativeResidual),runtime=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription});
    Write("COMPLETE.json",new{seed,mode,commit,status="EXECUTED_NUMERIC_NOT_GEOGRAPHIC",fields=fields.Count});File.Delete(Path.Combine(root,"INCOMPLETE.json"));
    Console.WriteLine($"WORLD COMPLETE seed={seed} mode={mode} maximumY={h.Final.ElevationKm.Max()*12+168:R} solves={result.Solves.Count}");
    return 0;
    void Save(string name,IReadOnlyList<double> data,string unit)
    {
        if(data.Count!=side*side||data.Any(v=>!double.IsFinite(v)))throw new ArithmeticException("Invalid field "+name);
        byte[] bytes=new byte[data.Count*8];for(int i=0;i<data.Count;i++)BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i*8,8),data[i]);
        using(var stream=new FileStream(Path.Combine(root,name+".f64le"),FileMode.CreateNew))stream.Write(bytes);
        fields.Add(name,new{path=name+".f64le",sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),unit});
    }
}
catch(Exception ex){Write("FAILED.json",new{seed,mode,commit,error=ex.ToString(),geographicAcceptance="NOT_ACCEPTED"});Console.Error.WriteLine(ex);return 1;}
void Write(string path,object data){using var file=new FileStream(Path.Combine(root,path),FileMode.CreateNew);JsonSerializer.Serialize(file,data,json);}
