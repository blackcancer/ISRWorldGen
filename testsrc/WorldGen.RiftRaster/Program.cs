using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

var scale = new TectonicScalePlan(1_000_000, 1_000_000);
var names = new List<string>();
var failures = new List<string>();
var numbers = new Dictionary<string, double>();
var json = new JsonSerializerOptions { WriteIndented = true };
void Near(double a, double b, double tolerance = 1e-9)
{
    if (!double.IsFinite(a + b) || Math.Abs(a - b) > tolerance * Math.Max(1, Math.Abs(b)))
        throw new ArithmeticException($"Mismatch {a:R} / {b:R}");
}
void Check(string name, Action test)
{
    try { test(); names.Add(name); Console.Error.WriteLine("PASS " + name); }
    catch (Exception e) { failures.Add(name + ": " + e); Console.Error.WriteLine("FAIL " + name + " " + e.Message); }
}
void Refuses(Action test)
{
    try { test(); } catch (ArgumentException) { return; }
    throw new Exception("Expected explicit input/topology rejection.");
}
RiftNecking Make(double end = 60, bool pause = false)
{
    RiftLoadingPhase[] phases = pause
        ? [new(0, 40, -1800, 2200), new(40, 50, 300, 300), new(50, end, -1200, 2800)]
        : [new(0, end, -1800, 2200)];
    return new([new(10, 100000, 35, 4, 3), new(20, 50000, 28, 1, 3), new(30, 150000, 40, 6, 3)],
        phases, 300000, 200000, .01, 7);
}
RiftPlacement Place(string id = "fixture", double nx = 1, double nz = 0, double ox = 0, double oz = 100000,
    double end = 60, bool pause = false) => new(id, Make(end, pause), ox, oz, nx, nz);
RiftMaterialRaster Raster(RiftPlacement p, int n = 32, bool periodic = true)
    => RiftMaterialRaster.Project([p], scale, n, periodic);
void Inventories(RiftMaterialRaster r, RiftPlacement p)
{
    var section = p.History.Sample(p.History.ObservationTimeMyr);
    Near(r.ContinentalKm.Sum() * r.CellAreaKm2, section.ContinentalVolumeKm3);
    Near(r.OceanicKm.Sum() * r.CellAreaKm2, section.NewOceanVolumeKm3);
    Near(r.OceanFraction.Sum() * r.CellAreaKm2, section.NewOceanAreaKm2);
    for (int k = 0; k < r.Patches.Count; k++)
    {
        var patch = r.Patches[k]; var packets = r.Packets.Where(v => v.Patch == k).ToArray();
        double area = packets.Sum(v => v.AreaFraction) * r.CellAreaKm2;
        Near(area, patch.AreaReference2 * r.ReferenceKmPerUnit * r.ReferenceKmPerUnit);
        Near(packets.Sum(v => v.AreaFraction * v.MeanAgeMyr) * r.CellAreaKm2, area * patch.MeanAgeMyr);
    }
}
var p0 = Place();
var r0 = Raster(p0);
Check("continental origin and new ocean volume conserved independently", () => Inventories(r0, p0));
Check("initial thin continental crust is never erased or relabelled as ocean", () =>
{
    foreach (var original in p0.History.Ribbons)
    {
        double volume = r0.Packets.Where(p => r0.Patches[p.Patch].Material == "continental"
            && r0.Patches[p.Patch].OriginOrEventId == original.OriginId)
            .Sum(p => p.AreaFraction * r0.Patches[p.Patch].ThicknessKm) * r0.CellAreaKm2;
        Near(volume, original.WidthReference * original.CrustKm * 200000 * .0001);
    }
});
Check("oblique cut cells conserve areas volumes and affine ages", () => { var p = Place(nx:.8,nz:.6); Inventories(Raster(p),p); });
Check("arbitrary frame angle conserves the same material inventories", () => { var p=Place(nx:Math.Cos(.731),nz:Math.Sin(.731));Inventories(Raster(p),p); });
Check("negative normal preserves material inventories", () => { var p=Place(nx:-1,ox:1000000,oz:300000);Inventories(Raster(p),p); });
Check("quarter turn rotates the real footprint", () => { var p=Place(nx:0,nz:1,ox:500000,oz:0);Inventories(Raster(p),p); });
Check("periodic crossing preserves complete polygons rather than cropping", () => { var p=Place(nx:.8,nz:.6,ox:600000,oz:650000);Inventories(Raster(p),p); });
Check("wrapped translation by a full atlas leaves all cell quantities unchanged", () =>
{
    var r=Raster(Place(ox:1000000,oz:1100000));
    for(int i=0;i<r0.OceanicKm.Count;i++){Near(r.OceanicKm[i],r0.OceanicKm[i]);Near(r.ContinentalKm[i],r0.ContinentalKm[i]);Near(r.OceanAgeMomentKmMyr[i],r0.OceanAgeMomentKmMyr[i]);}
});
Check("nonperiodic cropped material is refused", () => Refuses(()=>Raster(Place(ox:800000),periodic:false)));
Check("contained nonperiodic and periodic projections agree", () =>
{
    var r=Raster(p0,periodic:false);for(int i=0;i<r0.OceanicKm.Count;i++)Near(r.OceanicKm[i],r0.OceanicKm[i]);
});
Check("positive overlap is refused even when total cell coverage is below one", () =>
    Refuses(()=>RiftMaterialRaster.Project([p0, Place(id:"other",ox:1)],scale,8)));
Check("duplicate footprint identities are refused", () => Refuses(()=>RiftMaterialRaster.Project([p0,p0],scale,32)));
Check("touching fragment boundaries do not double material", () =>
{
    var p=Place(end:20);var r=Raster(p);Inventories(r,p);if(r.OceanicKm.Any(v=>v!=0))throw new Exception("Ocean before breakup.");
});
Check("dormant intervals transport old floor without making new sources", () =>
{
    var p=Place(pause:true);var r=Raster(p);Inventories(r,p);
    if(r.Patches.Where(v=>v.Material=="new-ocean").Any(v=>v.OriginOrEventId==1))throw new Exception("Dormant source.");
});
Check("grid refinement preserves global inventories and age moments", () =>
{
    var p=Place(nx:.8,nz:.6);var a=Raster(p,16);var b=Raster(p,128);
    Near(a.OceanicKm.Sum()*a.CellAreaKm2,b.OceanicKm.Sum()*b.CellAreaKm2);
    Near(a.OceanAgeMomentKmMyr.Sum()*a.CellAreaKm2,b.OceanAgeMomentKmMyr.Sum()*b.CellAreaKm2);
});
Check("coarse cell quantities equal area aggregates of refined cells", () =>
{
    var p=Place(nx:.8,nz:.6);var a=Raster(p,16);var b=Raster(p,32);
    for(int z=0;z<16;z++)for(int x=0;x<16;x++){
        int i=z*16+x,j=2*z*32+2*x;
        Near(a.OceanicKm[i],(b.OceanicKm[j]+b.OceanicKm[j+1]+b.OceanicKm[j+32]+b.OceanicKm[j+33])/4);
        Near(a.OceanAgeMomentKmMyr[i],(b.OceanAgeMomentKmMyr[j]+b.OceanAgeMomentKmMyr[j+1]+b.OceanAgeMomentKmMyr[j+32]+b.OceanAgeMomentKmMyr[j+33])/4);
    }
});
Check("subcell ocean opening is retained when no cell centre enters it", () =>
{
    var h=new RiftNecking([new(1,20000,35,1,2)],[new(0,10.05,-1000,1000)],400000,150000,.01,7);
    var p=new RiftPlacement("narrow",h,0,100000,1,0);var r=Raster(p,128);Inventories(r,p);
    var timelines=RiftSpreadingAdapter.ToTimelines(h,"narrow",0,100000,1,0);
    int hits=0;for(int z=0;z<128;z++)for(int x=0;x<128;x++)
        if(timelines.Any(t=>t.TryResolveBirth((x+.5)*1000000/128,(z+.5)*1000000/128,out _)))hits++;
    if(hits!=0||r.OceanFraction.Sum()<=0)throw new Exception("Narrow regression not reproduced.");
    numbers["narrowCellCentreHits"]=hits;numbers["narrowResolvedOceanAreaKm2"]=r.OceanFraction.Sum()*r.CellAreaKm2;
});
Check("multiple sources within a cell retain distinct packets", () =>
{
    var r=Raster(Place(pause:true),8);
    if(!r.Packets.GroupBy(p=>p.Cell).Any(g=>g.Where(p=>r0.Patches.Count>=0&&r.Patches[p.Patch].Material=="new-ocean").Select(p=>p.Patch).Distinct().Count()>1))
        throw new Exception("Missing multi-source cut cell.");
});
Check("uncovered atlas is unknown and not a fabricated seven kilometre ocean", () =>
{
    if(!Enumerable.Range(0,r0.OceanicKm.Count).Any(i=>r0.ContinentalFraction[i]+r0.OceanFraction[i]==0&&r0.OceanicKm[i]==0))throw new Exception("No unknown space.");
    if(r0.ContinentalFraction.Sum()+r0.OceanFraction.Sum()>=r0.Side*r0.Side)throw new Exception("Full atlas invented.");
});
Check("physical reference atlas unchanged when resized to smaller game maps", () =>
{
    foreach(long size in new long[]{131072,262144}){
        var r=RiftMaterialRaster.Project([p0],new TectonicScalePlan(size,size),32);
        if(r.Checksum!=r0.Checksum)throw new Exception("Reference atlas cropped or regenerated.");
    }
});
Check("rectangle reference metric does not lose material", () =>
{
    var r=RiftMaterialRaster.Project([p0],new TectonicScalePlan(1000000,750000),32);Inventories(r,p0);
});
Check("invalid frame and nonfinite coordinates are refused", () =>
{
    Refuses(()=>Raster(p0 with{NormalX=2}));Refuses(()=>Raster(p0 with{OriginX=double.NaN}));
});
Check("mixed observation dates are refused", () =>Refuses(()=>RiftMaterialRaster.Project([p0,Place(id:"old",end:20)],scale,32)));
Check("duplicate material overlaps are refused regardless of caller order", () =>
{
    var p=Place(id:"other",ox:100);Refuses(()=>RiftMaterialRaster.Project([p,p0],scale,32));
});
Check("disjoint placements are invariant under caller permutation", () =>
{
    var p=Place(id:"second",oz:500000);
    var a=RiftMaterialRaster.Project([p0,p],scale,32);var b=RiftMaterialRaster.Project([p,p0],scale,32);
    if(a.Checksum!=b.Checksum)throw new Exception("Order-dependent raster.");
});
Check("unsupported grid sizes fail before producing a raster", () =>Refuses(()=>Raster(p0,17)));
Check("packet ages stay inside their actual event intervals", () =>
{
    foreach(var packet in r0.Packets)if(packet.MeanAgeMyr<0||packet.MeanAgeMyr>60-p0.History.BreakupTimeMyr!.Value+1e-9)throw new Exception("Invalid age.");
});
Check("same physical inputs reproduce the exact packet checksum", () =>
{
    if(Raster(Place()).Checksum!=r0.Checksum)throw new Exception("Non deterministic projection.");
});
Check("complete periodic-axis extrusion conserves the shared border", () =>
{
    var h=new RiftNecking([new(10,100000,35,4,5),new(20,50000,28,1,3),new(30,150000,40,6,6)],
        [new(0,60,-1500,2500)],300000,1000000,.01,7);
    var p=new RiftPlacement("prior-csharp-diagnostic",h,0,0,1,0);var r=Raster(p,128);Inventories(r,p);
    Near(r.OceanFraction.Sum()*r.CellAreaKm2,10125000);
});
Check("physical area conversion changes inventories not projected fractions", () =>
{
    var h=new RiftNecking(p0.History.Ribbons,p0.History.Loading,300000,200000,.02,7);
    var p=p0 with {History=h};var r=Raster(p);Inventories(r,p);
    Near(r.CellAreaKm2,4*r0.CellAreaKm2);
    for(int i=0;i<r0.OceanicKm.Count;i++){Near(r.ContinentalKm[i],r0.ContinentalKm[i]);Near(r.OceanAgeMomentKmMyr[i],r0.OceanAgeMomentKmMyr[i]);}
});
Check("touching independent placements remain disjoint at the shared edge", () =>
{
    var r=RiftMaterialRaster.Project([p0,Place(id:"touch",oz:300000)],scale,32);
    Near(r.ContinentalKm.Sum(),2*r0.ContinentalKm.Sum());Near(r.OceanAgeMomentKmMyr.Sum(),2*r0.OceanAgeMomentKmMyr.Sum());
});
Check("all fields reconstruct from separate material packets", () =>
{
    var r=Raster(Place(nx:.8,nz:.6,pause:true));var arrays=Enumerable.Range(0,5).Select(_=>new double[r.Side*r.Side]).ToArray();
    foreach(var packet in r.Packets){var p=r.Patches[packet.Patch];int i=packet.Cell;double a=packet.AreaFraction;
        if(p.Material=="continental"){arrays[0][i]+=a*p.ThicknessKm;arrays[3][i]+=a;}
        else{arrays[1][i]+=a*p.ThicknessKm;arrays[2][i]+=a*p.ThicknessKm*packet.MeanAgeMyr;arrays[4][i]+=a;}}
    IReadOnlyList<double>[] actual=[r.ContinentalKm,r.OceanicKm,r.OceanAgeMomentKmMyr,r.ContinentalFraction,r.OceanFraction];
    for(int k=0;k<5;k++)for(int i=0;i<arrays[k].Length;i++)Near(arrays[k][i],actual[k][i]);
});
Check("axis aligned age moments match an independent rectangle centroid oracle", () =>
{
    var r=Raster(Place(pause:true));double dx=r.WidthReference/r.Side,dz=r.LengthReference/r.Side;
    for(int z=0;z<r.Side;z++)for(int x=0;x<r.Side;x++){
        double ocean=0,moment=0;
        foreach(var p in r.Patches.Where(p=>p.Material=="new-ocean")){
            var v=p.SourceVertices;double l=v.Min(a=>a.X),rr=v.Max(a=>a.X),t=v.Min(a=>a.Z),b=v.Max(a=>a.Z);
            double left=Math.Max(x*dx,l),right=Math.Min((x+1)*dx,rr),top=Math.Max(z*dz,t),bottom=Math.Min((z+1)*dz,b);
            if(left>=right||top>=bottom)continue;
            var lv=v.First(a=>a.X==l);var rv=v.First(a=>a.X==rr);
            double age=lv.AgeMyr+((left+right)/2-l)/(rr-l)*(rv.AgeMyr-lv.AgeMyr);
            double column=(right-left)*(bottom-top)/(dx*dz)*p.ThicknessKm;ocean+=column;moment+=column*age;
        }
        Near(ocean,r.OceanicKm[z*r.Side+x]);Near(moment,r.OceanAgeMomentKmMyr[z*r.Side+x]);
    }
});
if(failures.Count>0){Console.WriteLine(JsonSerializer.Serialize(new{passed=names.Count,failed=failures.Count,names,failures},json));return 1;}
var report=new{algorithm=RiftMaterialRaster.AlgorithmId,passed=names.Count,failed=0,names,numbers,geographicAcceptance="NOT_EVALUATED",erosion="NOT_RUN"};
Console.WriteLine(JsonSerializer.Serialize(report,json));
if(args.Length==0)return 0;
if(args.Length!=1)throw new ArgumentException("Usage: RiftRaster [NEW_OUTPUT_DIRECTORY]");
string root=Path.GetFullPath(args[0]);
if(Directory.Exists(root)||File.Exists(root)){Console.Error.WriteLine("REFUSED_EXISTING_EVIDENCE");return 2;}
Directory.CreateDirectory(root);
Write("INCOMPLETE.json",new{algorithm=RiftMaterialRaster.AlgorithmId,status="INCOMPLETE"});
try{
    var fixtures=new[]{Place(id:"axial"),Place(id:"oblique",nx:.8,nz:.6,pause:true),Place(id:"periodic",nx:.8,nz:.6,ox:600000,oz:650000)};
    var summaries=new List<object>();
    foreach(var p in fixtures){
        string dir=p.Identity;Directory.CreateDirectory(Path.Combine(root,dir));
        var r=Raster(p,512);Inventories(r,p);
        var fields=new Dictionary<string,object>();
        Save("continental-thickness",r.ContinentalKm,"equivalent km continental crust");
        Save("oceanic-thickness",r.OceanicKm,"equivalent km new oceanic crust");
        Save("age-moment",r.OceanAgeMomentKmMyr,"equivalent km times model Myr");
        Save("continental-fraction",r.ContinentalFraction,"area fraction");
        Save("ocean-fraction",r.OceanFraction,"area fraction");
        Write(dir+"/packets.json",new{r.Patches,r.Packets});
        Write(dir+"/geometry.json",new{p.Identity,p.OriginX,p.OriginZ,p.NormalX,p.NormalZ,
            observation=p.History.ObservationTimeMyr,alongLength=p.History.AlongRiftLengthReference,
            oceanThickness=p.History.NewOceanicThicknessKm,section=p.History.Sample(p.History.ObservationTimeMyr),phases=p.History.SpreadingPhases()});
        Write(dir+"/manifest.json",new{algorithm=RiftMaterialRaster.AlgorithmId,commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"LOCAL",
            fixture=p.Identity,seed=(int?)null,profile="CONTROLLED_RUPTURE_NOT_WORLD_GENERATION",width=r.Side,height=r.Side,
            r.WidthReference,r.LengthReference,r.ReferenceKmPerUnit,cellSpacingReference=r.WidthReference/r.Side,
            originX=0,originZ=0,projection="periodic planar atlas; X right Z down",fields,
            altitudePresent=false,waterSurfacePresent=false,unknownPolicy="1-continental-fraction-ocean-fraction; no background material invented",
            geographicAcceptance="NOT_EVALUATED",erosion="NOT_RUN",r.Checksum});
        summaries.Add(new{fixture=p.Identity,r.Checksum,continentalVolume=r.ContinentalKm.Sum()*r.CellAreaKm2,
            newOceanVolume=r.OceanicKm.Sum()*r.CellAreaKm2,ageMoment=r.OceanAgeMomentKmMyr.Sum()*r.CellAreaKm2,packetCount=r.Packets.Count});
        void Save(string name,IReadOnlyList<double> values,string units){
            byte[] bytes=new byte[values.Count*8];for(int i=0;i<values.Count;i++)BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i*8,8),values[i]);
            using(var f=new FileStream(Path.Combine(root,dir,name+".f64le"),FileMode.CreateNew))f.Write(bytes);
            fields.Add(name,new{path=name+".f64le",units,encoding="float64 little-endian",sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()});
        }
    }
    Write("COMPLETE.json",new{status="CONSERVATIVE_GEOMETRY_ONLY",report,summaries});File.Delete(Path.Combine(root,"INCOMPLETE.json"));
    return 0;
}catch(Exception e){Write("FAILED.json",new{error=e.ToString(),status="FAIL"});Console.Error.WriteLine(e);return 1;}
void Write(string name,object value){using var f=new FileStream(Path.Combine(root,name),FileMode.CreateNew);JsonSerializer.Serialize(f,value,json);}
