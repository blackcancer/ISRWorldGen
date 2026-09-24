using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
if (args.Length == 0 || args is ["--self-test"])
{
    Console.WriteLine(JsonSerializer.Serialize(DrivingChecks.Run(), json));
    return 0;
}
if (args[0] != "generate" || args.Length is not (3 or 6 or 8))
    throw new ArgumentException("Usage: generate NEW_OUTPUT SEED [RESOLUTION DURATION_MYR stationary|evolving [WIDTH_BLOCKS LENGTH_BLOCKS]]; no arguments runs tests.");
string root = Path.GetFullPath(args[1]);
int seed = int.Parse(args[2], CultureInfo.InvariantCulture);
int side = args.Length >= 6 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 512;
double duration = args.Length >= 6 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 96;
string mode = args.Length >= 6 ? args[5] : "evolving";
if (mode is not ("stationary" or "evolving")) throw new ArgumentException("Unknown loading mode");
long width = args.Length == 8 ? long.Parse(args[6], CultureInfo.InvariantCulture) : 1000000;
long length = args.Length == 8 ? long.Parse(args[7], CultureInfo.InvariantCulture) : 1000000;
_ = new TectonicScalePlan(width, length); _ = new TectonicEvolutionSettings(side: side, duration: duration);
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Never overwrite prior evidence.");
// Publish once by same-volume directory rename. Interrupted computation retains
// its own INCOMPLETE staging directory and never impersonates a complete world.
string stage = root + ".partial-" + Guid.NewGuid().ToString("N");
Directory.CreateDirectory(stage);
string commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "LOCAL_UNCOMMITTED";
Write("INCOMPLETE.json", new { seed, mode, commit, side, duration, width, length });
try
{
    Console.Error.WriteLine($"Generating full atlas: seed={seed}, {side}x{side}, {duration} Myr model, {mode}");
    var result = IntegratedReliefGenerator.Generate(seed, width, length, side, duration, mode == "evolving");
    var world = result.World; var mechanical = world.MechanicalHistory; var history = mechanical.History;
    var height = world.ElevationKm.Select(v => 168 + 12*v).ToArray();
    var firstHeight = world.InitialElevationKm.Select(v => 168 + 12*v).ToArray();
    // Prepare all images before writing any, including range checks. No image
    // uses sea-level masking, smoothing or individual contrast stretching.
    var png = new Dictionary<string,byte[]> {
        ["height-16bit.png"] = NumericHeightPng.Encode(height,side),
        ["initial-height-16bit.png"] = NumericHeightPng.Encode(firstHeight,side),
        ["height-preview.png"] = NumericHeightPng.Encode(height,side,true),
        ["initial-height-preview.png"] = NumericHeightPng.Encode(firstHeight,side,true)
    };
    var fields = new SortedDictionary<string,object>(StringComparer.Ordinal);
    Save("height",height,"solid Y blocks"); Save("initial-height",firstHeight,"solid Y blocks");
    Save("elevation-model",world.ElevationKm,"signed km model relative to datum, including seabed");
    Save("continental-thickness",history.Final.ContinentalKm,"equivalent km");
    Save("oceanic-thickness",history.Final.OceanicKm,"equivalent km");
    Save("ocean-age",history.Final.OceanAge,"transported mean Myr model; uniform inherited prior not reconstructed history");
    Save("inherited-ocean",history.Final.InheritedOceanicKm,"equivalent km");
    Save("plastic-strain",Enumerable.Range(0,side*side).Select(mechanical.Memory.Strain).ToArray(),"dimensionless accumulated plastic excess");
    Save("pressure-potential",world.PotentialKm2,"km2, pressure integral divided by mantle density and g");
    Save("compression-history",history.Final.AccumulatedCompression,"fixed-grid integrated convergence; not parcel strain");
    Save("extension-history",history.Final.AccumulatedExtension,"fixed-grid integrated divergence; not rupture");
    Write("ledger.json",history.Ledger); Write("solves.json",mechanical.Solves); Write("driving-history.json",result.LoadHistory);
    Directory.CreateDirectory(Path.Combine(stage,"png"));
    foreach(var (name,bytes) in png) using(var file=new FileStream(Path.Combine(stage,"png",name),FileMode.CreateNew)) file.Write(bytes);
    Write("manifest.json",new {
        commit,seed,mode,algorithm=IntegratedReliefGenerator.AlgorithmId,settings=result.Settings,driving=result.Driving,
        densityResponse=world.Options,width=side,height=side,mechanicalSide=Math.Min(128,side),worldWidthBlocks=width,worldLengthBlocks=length,
        referenceWidth=result.Scale.ReferenceWidth,referenceLength=result.Scale.ReferenceLength,
        referenceKmPerUnit=MaterialBoundHistory.ReferenceKmPerUnit,sampleStepBlocksX=width/(double)side,sampleStepBlocksZ=length/(double)side,
        history.InitialMaterialChecksum,history.FinalMaterialChecksum,world.Checksum,fields,
        initialOrigins=history.InitialContinentalByOrigin,finalOrigins=history.FinalContinentalByOrigin,
        heightDecode="Y = code * 383 / 65535",physicalHeightDecode="Y = 168 + 12 * elevationKmModel",
        png=png.ToDictionary(p=>p.Key,p=>Convert.ToHexString(SHA256.HashData(p.Value)).ToLowerInvariant()),
        maximumForceResidual=mechanical.Solves.Count>0?mechanical.Solves.Max(s=>s.RelativeResidual):0,
        erosion="NOT_RUN",nativeRuntime="NOT_RUN",geographicAcceptance="UNREVIEWED_NOT_A_NUMERICAL_TEST_RESULT",
        loadModel="DECLARED_EVOLVING_BASAL_DRIVING_PRIOR_NOT_A_SOLVED_MANTLE",
        oceanHistory="UNIFORM_INITIAL_AGE_PRIOR_WITH_ACTUAL_CONSERVATIVE_BIRTH_AND_RECYCLING; NO_NEW_FRACTURE_TOPOLOGY",
        runtime=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,
        coreAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IntegratedReliefGenerator).Assembly.Location))).ToLowerInvariant()
    });
    Write("COMPLETE.json",new {commit,seed,mode,status="EXECUTED_FULL_WORLD_NOT_GEOGRAPHIC_PASS",fieldCount=fields.Count});
    File.WriteAllText(Path.Combine(stage,"Galerie.html"),$$"""
        <!doctype html><html lang="fr"><meta charset="utf-8"><title>ISRWorldGen — relief {{seed}}</title>
        <style>body{font:16px system-ui;max-width:1200px;margin:32px auto;padding:0 20px}section{display:flex;gap:20px;flex-wrap:wrap}figure{margin:0;flex:1;min-width:320px}img{width:100%;image-rendering:pixelated}p{line-height:1.6}</style>
        <h1>Relief solide — seed {{seed}}</h1><p>Atlas entier : {{width:N0}} × {{length:N0}} blocs. Mesures : {{side}} × {{side}}, mécanique : {{Math.Min(128,side)}} × {{Math.Min(128,side)}}. Histoire : {{duration}} Myr modèle, {{mode}}.</p>
        <p>Échelle identique Y = 0 à 383, fonds marins inclus. Les aperçus sont en 8 bits ; les PNG numériques sont en 16 bits. Ni couche d’eau, ni érosion, ni contraste automatique. Ce résultat n’est pas une validation géographique.</p>
        <section><figure><h2>État initial</h2><img src="png/initial-height-preview.png"><p><a href="png/initial-height-16bit.png">Heightmap numérique initiale</a></p></figure><figure><h2>Après évolution</h2><img src="png/height-preview.png"><p><a href="png/height-16bit.png">Heightmap numérique finale</a></p></figure></section>
        <p><a href="manifest.json">Paramètres et empreintes</a> · <a href="elevation-model.f64le">Altitudes natives signées</a> · <a href="driving-history.json">Historique des mouvements imposés</a> · <a href="ledger.json">Bilans de matière</a></p>
        </html>
        """);
    File.Delete(Path.Combine(stage,"INCOMPLETE.json"));
    Directory.Move(stage,root); // Fails rather than replacing an existing output.
    Console.WriteLine($"WORLD COMPLETE: {root}; Y=[{height.Min():R},{height.Max():R}]");
    return 0;
    void Save(string name,IReadOnlyList<double> data,string unit)
    {
        if(data.Count!=side*side||data.Any(v=>!double.IsFinite(v)))throw new ArithmeticException("Invalid field "+name);
        var bytes=new byte[data.Count*8];for(int i=0;i<data.Count;i++)BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i*8,8),data[i]);
        using(var file=new FileStream(Path.Combine(stage,name+".f64le"),FileMode.CreateNew))file.Write(bytes);
        fields.Add(name,new{path=name+".f64le",sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),unit});
    }
}
catch(Exception ex)
{
    if(Directory.Exists(stage))Write("FAILED.json",new{commit,seed,mode,error=ex.ToString(),stage});
    Console.Error.WriteLine(ex);return 1;
}
void Write(string name,object data){using var file=new FileStream(Path.Combine(stage,name),FileMode.CreateNew);JsonSerializer.Serialize(file,data,json);}
