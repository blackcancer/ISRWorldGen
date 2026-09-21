using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

if (args.Length != 4 || !int.TryParse(args[1],out int side) || side is <64 or >512 || (side&(side-1))!=0 ||
    !int.TryParse(args[2],out int seed) || args[3]!="powerlaw")
    throw new ArgumentException("Usage: WorldGen.OceanCooling <new-directory> <side64..512> <seed> powerlaw");
string root=Path.GetFullPath(args[0]);
if(Directory.Exists(root)||File.Exists(root))throw new IOException("Evidence directory already exists.");
string[] checks=TectonicChecks.Run().Concat(PolarityRegressionChecks.Run()).Concat(MaterialCohortChecks.Run())
    .Concat(OceanCarrierChecks.Run()).Concat(AssemblageChecks.Run()).Concat(DeformationChecks.Run())
    .Concat(PowerLawChecks.Run()).Concat(OceanCoolingChecks.Run()).ToArray();
Directory.CreateDirectory(root);
var json=new JsonSerializerOptions{WriteIndented=true};
var scale=new TectonicScalePlan(1_000_000,1_000_000);var settings=new TectonicEvolutionSettings(side:side);
var oldInitial=MaterialBoundHistory.Generate(seed,scale,new TectonicEvolutionSettings(side:side,duration:0));
var assemblage=ContinentalAssemblage.Generate(seed,scale,side,CrustTransport.Sum(oldInitial.Initial.ContinentalKm)/(side*side));
var rheology=new SheetRheologyOptions(powerLaw:new PowerLawSheetOptions());var cooling=new OceanCoolingOptions();
var history=MaterialBoundHistory.GenerateWithOceanCooling(seed,scale,settings,assemblage,rheology,cooling);
var end=history.Final;var frame=history.FinalDeformation!;
string output=Path.Combine(root,"seed-"+seed.ToString(System.Globalization.CultureInfo.InvariantCulture));Directory.CreateDirectory(output);
var fields=new Dictionary<string,object>();
Field("initial-height",history.Initial.ElevationKm.Select(Y),"initial solid Y; same datum for every seed");
Field("height",end.ElevationKm.Select(Y),"solid Y from transported thermal spectrum; no water mask");
Field("legacy-height",end.ContinentalKm.Select((c,i)=>Y(CrustResponse.ElevationKm(c,end.OceanicKm[i],end.OceanAge[i]))),"control: old mean-age response on identical final material");
Field("mean-age-cooling-height",end.ContinentalKm.Select((c,i)=>Y(cooling.ElevationKm(c,end.OceanicKm[i],cooling.ColdFraction(end.OceanAge[i])))),"control: finite cooling of mean age, identical material");
Field("thermal-cold-fraction",history.FinalColdFraction!,"transported volume-weighted cold fraction, NOT cooling of average age");
Field("initial-thermal-cold-fraction",history.InitialColdFraction!,"initial thermal state; uniform inherited age not reconstructed");
Field("elevation-model",end.ElevationKm,"signed solid altitude in model km before projection into blocks");
Field("continental-thickness",end.ContinentalKm,"km equivalent continental crust");Field("oceanic-thickness",end.OceanicKm,"km equivalent oceanic crust");
Field("ocean-age",end.OceanAge,"mean model Myr of carried ocean volume, zero where absent");
Field("new-ocean-fraction",end.OceanicKm.Select((v,i)=>v>0?1-end.InheritedOceanicKm[i]/v:0),"fraction formed during this history");
Field("compression",end.AccumulatedCompression,"Eulerian accumulated negative divergence");Field("extension",end.AccumulatedExtension,"Eulerian accumulated positive divergence");
Field("shear",end.AccumulatedShear,"Eulerian accumulated shear");Field("plates",end.PlateIds.Select(v=>(double)v),"dominant material origin");
Field("owner-fraction",history.FinalOwnerFraction,"dominant origin fraction");
var scaleChecks=new List<object>();
foreach(long size in new long[]{131072,262144,1000000})
{
    var small=new TectonicScalePlan(size,size);double maximum=0;
    for(int z=0;z<side;z++)for(int x=0;x<side;x++)maximum=Math.Max(maximum,Math.Abs(history.SampleElevationKm(small,(x+.5)*size/side,(z+.5)*size/side)-end.ElevationKm[z*side+x]));
    if(maximum>1e-10)throw new ArithmeticException("Changing map size cropped or altered the entire atlas.");
    scaleChecks.Add(new{size,sameCompleteAtlas=true,crop=false,maximumDifferenceKm=maximum,sampleStepBlocks=size/(double)side});
}
int components=LargeComponents(end.ElevationKm,side);
var report=new
{
    seed,mode="powerlaw",algorithm="ocean-thermal-comparison-v1/"+MaterialBoundHistory.AlgorithmId,
    commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"UNVERIFIED_WORKTREE",
    scope="ONE_WAY_THERMAL_ISOSTASY_ON_SAME_MATERIAL_HISTORY",geographicAcceptance="NOT_ACCEPTED",
    nativeGame="NOT_RUN",erosion="NOT_RUN_GATED_ON_RELIEF_REVIEW",independentCodeReview="NOT_RUN",
    initialAssemblage=new{algorithm=ContinentalAssemblage.AlgorithmId,assemblage.Checksum,assemblage.Provinces,assemblage.MeanContinentalKm,matchedContinentalVolume=true},
    historyChecksum=history.Checksum,initialChecksum=history.Initial.Checksum,finalChecksum=end.Checksum,
    history.InitialMaterialChecksum,history.FinalMaterialChecksum,history.InitialContinentalByOrigin,history.FinalContinentalByOrigin,history.UnresolvedInterfaceFaces,
    settings,rheology,cooling,thermalAlgorithm=OceanCoolingOptions.AlgorithmId,history.ThermalReceipts,
    thermalApproximationBoundKm=cooling.ConservativeElevationErrorKm,
    thermalLimitation="constant properties and finite spectrum; unknown initial age geography NOT reconstructed; no thermal feedback on rheology or polarity",
    materialComparison="legacy, finite mean-age, transported thermal responses all use IDENTICAL final material; no contrast or level adjustment",
    worldWidthBlocks=1_000_000,worldLengthBlocks=1_000_000,history.ReferenceWidth,history.ReferenceLength,
    referenceKmPerUnit=MaterialBoundHistory.ReferenceKmPerUnit,worldHeightBlocks=384,seaLevelReferenceBlocks=168,blocksPerModelKm=12,
    width=side,height=side,fields,scaleChecks,plates=history.Plates,mechanicalSide=frame.MechanicalSide,
    mechanicalPolicy=SheetRheologyOptions.AlgorithmId,equilibriumOperator=PowerLawSheetDeformation.AlgorithmId,
    mechanicalSolves=history.MechanicalSolves,maxForceResidual=history.MechanicalSolves.Max(s=>s.RelativeResidual),
    waterSurfacePresent=false,seabedMasked=false,perImageAutoContrast=false,
    boundary="PERIODIC_PLANAR_PREPARATORY_ATLAS_NOT_NATIVE_PLAYER_WRAPPING",largeLandComponents=components,
    steps=history.Ledger.Count-1,initial=history.Ledger[0],final=history.Ledger[^1],
    solidMin=Y(end.ElevationKm.Min()),solidMax=Y(end.ElevationKm.Max()),landFraction=end.ElevationKm.Count(v=>v>=0)/(double)(side*side),
    coreAssemblySha256=Hash(File.ReadAllBytes(typeof(MaterialBoundHistory).Assembly.Location)),
    runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,os=System.Runtime.InteropServices.RuntimeInformation.OSDescription
};
File.WriteAllText(Path.Combine(output,"history-ledger.json"),JsonSerializer.Serialize(history.Ledger,json));
File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(report,json));
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{status="PASS",checks=checks.Length,names=checks},json));
File.WriteAllText(Path.Combine(root,"verification.json"),JsonSerializer.Serialize(new{status="PASS",tests=checks.Length,checks,geographicAcceptance="NOT_ACCEPTED",reports=new[]{report}},json));
Console.WriteLine($"CHECKS={checks.Length}; seed={seed}; complete {side}x{side} thermal history; no geographic acceptance.");
void Field(string name,IEnumerable<double> source,string unit)
{
    double[] values=source.ToArray();if(values.Length!=side*side||values.Any(v=>!double.IsFinite(v)))throw new ArithmeticException("Invalid field: "+name);
    if(name.EndsWith("height")&&values.Any(v=>v<0||v>383))throw new ArithmeticException("Solid height outside fixed envelope; no clamp.");
    byte[] bytes=new byte[8*values.Length];for(int i=0;i<values.Length;i++)BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(8*i,8),values[i]);
    string file=name+".f64le";using(var s=new FileStream(Path.Combine(output,file),FileMode.CreateNew))s.Write(bytes);
    fields.Add(name,new{path=file,unit,sha256=Hash(bytes),min=values.Min(),max=values.Max()});
}
static double Y(double h)=>168+12*h;
static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
static int LargeComponents(IReadOnlyList<double> h,int n)
{
    bool[] seen=new bool[h.Count];var queue=new Queue<int>();int large=0;
    for(int i=0;i<h.Count;i++)
    {
        if(seen[i]||h[i]<0)continue;seen[i]=true;queue.Enqueue(i);int size=0;
        while(queue.TryDequeue(out int p))
        {
            size++;int x=p%n,z=p/n;
            foreach(int j in new[]{z*n+(x+1)%n,z*n+(x+n-1)%n,((z+1)%n)*n+x,((z+n-1)%n)*n+x})
                if(!seen[j]&&h[j]>=0){seen[j]=true;queue.Enqueue(j);}
        }
        if(size>=Math.Max(4,h.Count/100))large++;
    }
    return large;
}
