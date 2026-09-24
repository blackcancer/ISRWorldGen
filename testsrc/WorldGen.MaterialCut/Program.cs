using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

var scale = new TectonicScalePlan(1_000_000, 1_000_000);
var names = new List<string>(); var errors = new List<string>();
var json = new JsonSerializerOptions { WriteIndented = true };
if (args.Length > 1) throw new ArgumentException("Usage: MaterialCut [NEW_OUTPUT_DIRECTORY]");
if (args.Length == 1 && (Directory.Exists(args[0]) || File.Exists(args[0])))
{ Console.Error.WriteLine("REFUSED_EXISTING_EVIDENCE"); return 2; }
void Check(string name, Action action)
{
    try { action(); names.Add(name); Console.Error.WriteLine("PASS " + name); }
    catch (Exception e) { errors.Add(name + ": " + e); Console.Error.WriteLine("FAIL " + name + ": " + e.Message); }
}
void True(bool ok) { if (!ok) throw new ArithmeticException("Assertion failed."); }
void Near(double a, double b, double tol = 1e-9)
{ if (!double.IsFinite(a + b) || Math.Abs(a - b) > tol * Math.Max(1, Math.Abs(b))) throw new ArithmeticException($"{a:R} != {b:R}"); }
void Refuse(Action action)
{ try { action(); } catch (ArgumentException) { return; } throw new ArithmeticException("Missing explicit input/topology refusal."); }
(MaterialMeshNode[] N, MaterialMeshFace[] F, MaterialLinkFailure[] E) Input(bool crooked = true, double angle = 0, double offset = 0)
{
    double[] bend = crooked ? [0, 20000, -15000, 40000, 10000, -10000, 0] : new double[7];
    var ns = new List<MaterialMeshNode>(); var fs = new List<MaterialMeshFace>(); var es = new List<MaterialLinkFailure>();
    for (int z = 0; z <= 6; z++) for (int x = 0; x <= 4; x++)
    {
        double px = new double[] { 150000, 300000, 500000 + bend[z], 700000, 850000 }[x] - 500000;
        double pz = 200000 + z * 100000 - 500000;
        ns.Add(new(z * 5 + x, 500000 + offset + Math.Cos(angle) * px - Math.Sin(angle) * pz,
            500000 + Math.Sin(angle) * px + Math.Cos(angle) * pz));
    }
    for (int z = 0; z < 6; z++) for (int x = 0; x < 4; x++)
    {
        int a = z * 5 + x, b = a + 1, c = a + 6, d = a + 5;
        int origin = x < 2 ? 10 + z % 2 : 20 + z % 3;
        fs.Add(new(fs.Count, origin, 30 + z + x, a, b, c)); fs.Add(new(fs.Count, origin, 30 + z + x, a, c, d));
    }
    for (int z = 0; z < 6; z++) es.Add(new(z * 5 + 2, (z + 1) * 5 + 2, z == 2 ? 1 : 2, "provided-mechanical-event-" + z));
    return (ns.ToArray(), fs.ToArray(), es.ToArray());
}
MaterialCutTopology Mesh(bool crooked = true, double angle = 0, double offset = 0)
{ var i = Input(crooked, angle, offset); return new(i.N, i.F, i.E); }
FragmentMotionPhase[] Loading(bool pause = false, double angle = 0)
{
    FragmentMotionPhase Phase(int id, double a, double b, double left, double right) => new(id, a, b,
        Math.Cos(angle) * left, Math.Sin(angle) * left, Math.Cos(angle) * right, Math.Sin(angle) * right);
    return pause ? [Phase(40, 2, 5, -1500, 2500), Phase(41, 5, 8, 300, 300), Phase(42, 8, 12, -1200, 2800)]
        : [Phase(40, 2, 12, -1500, 2500)];
}
MaterialOpeningProjection Open(MaterialCutTopology m, int side = 32, bool pause = false, double angle = 0,
    double km = .01, double ridge = .5) => m.Open(m.At(2), Loading(pause, angle), scale, side, km, 7, ridge);
var mesh = Mesh(); var snapshot = mesh.At(2);
Check("intact mesh is a connected material domain", () => True(mesh.At(0).Fragments.Count == 1));
Check("arrested interior cut does not separate a continent", () => { var s = mesh.At(1.5); True(s.Fragments.Count == 1 && s.InternalFailedLinks == 1); });
Check("last through-cut link determines first disconnection", () => { True(snapshot.Fragments.Count == 2); Near(snapshot.FirstSeparationTimeMyr!.Value, 2); True(snapshot.SeparatingEdges.Count == 6); });
Check("unbroken material cannot emit a conjugate ocean", () => Refuse(() => mesh.Open(mesh.At(1), Loading(), scale, 32, .01, 7)));
Check("mesh and failure input ordering cannot change identities", () => { var i = Input(); var m = new MaterialCutTopology(i.N.Reverse().ToArray(), i.F.Reverse().ToArray(), i.E.Reverse().ToArray()); True(mesh.Checksum == m.Checksum && snapshot.Checksum == m.At(2).Checksum); });
Check("triangle winding and cyclic vertex order are canonical", () => { var i = Input(); var m = new MaterialCutTopology(i.N, i.F.Select(f => f with { A=f.C, B=f.B, C=f.A }).ToArray(), i.E); True(mesh.Checksum == m.Checksum); });
Check("failure endpoint order preserves identity", () => { var i = Input(); var m = new MaterialCutTopology(i.N, i.F, i.E.Select(e => e with { NodeA=e.NodeB, NodeB=e.NodeA }).ToArray()); True(mesh.Checksum == m.Checksum); });
Check("invalid or duplicate vertices are refused", () => { var i=Input(); Refuse(()=>new MaterialCutTopology(i.N.Concat([i.N[0]]).ToArray(),i.F,i.E)); Refuse(()=>new MaterialCutTopology(i.N.Select((n,k)=>k==0?n with { X=double.NaN }:n).ToArray(),i.F,i.E)); });
Check("missing mechanical evidence and boundary failures are refused", () => { var i=Input(); Refuse(()=>new MaterialCutTopology(i.N,i.F,[new(0,1,2,"boundary")])); Refuse(()=>new MaterialCutTopology(i.N,i.F,[i.E[0] with { EvidenceId="" }])); });
Check("duplicate or negative-time failures are refused", () => { var i=Input(); Refuse(()=>new MaterialCutTopology(i.N,i.F,[i.E[0],i.E[0]])); Refuse(()=>new MaterialCutTopology(i.N,i.F,[i.E[0] with { TimeMyr=-1 }])); });
Check("nonmanifold and degenerate faces are refused", () => { var i=Input(); Refuse(()=>new MaterialCutTopology(i.N,i.F.Concat([i.F[0] with { Id=1000 }]).ToArray(),i.E)); Refuse(()=>new MaterialCutTopology(i.N,i.F.Select((f,k)=>k==0?f with { C=f.B }:f).ToArray(),i.E)); });
Check("all triangles survive separation with their own origin", () => { var r=Open(mesh); True(r.Sources.Count(p=>p.Material=="continental")==mesh.Faces.Count); foreach(var v in r.Volumes)Near(v.InitialKm3,v.ProjectedKm3); });
Check("crooked seam conserves exact swept ocean area", () => Near(Open(mesh).ExpectedOceanAreaKm2, 2400000));
Check("two conjugate flanks retain separate birth packets", () => { var r=Open(mesh); True(r.Sources.Where(s=>s.Material=="new-ocean").Select(s=>s.Flank).Distinct().Order().SequenceEqual(new[]{-1,1})); });
Check("common translation of separated fragments creates no basalt", () => { var r=mesh.Open(snapshot,[new(40,2,12,100,200,100,200)],scale,32,.01,7); Near(r.Raster.OceanicKm.Sum(),0); });
Check("tangential motion on a straight seam creates no ocean area", () => { var m=Mesh(false); var r=m.Open(m.At(2),[new(40,2,12,0,-100,0,100)],scale,32,.01,7); Near(r.ExpectedOceanAreaKm2,0); });
Check("closing a broken interface is refused rather than filled", () => Refuse(()=>mesh.Open(snapshot,[new(40,2,12,200,0,-200,0)],scale,32,.01,7)));
Check("pause creates no material and preserves ageing", () => { var r=Open(mesh,pause:true); Near(r.ExpectedOceanAreaKm2,1680000); True(!r.Sources.Any(s=>s.Material=="new-ocean"&&s.OriginOrEventId==41)); Near(r.Raster.OceanAgeMomentKmMyr.Sum()*r.Raster.CellAreaKm2,r.ExpectedOceanAgeMomentKm3Myr); });
Check("nonuniform ridge sharing preserves total area and age", () => { var a=Open(mesh); var b=Open(mesh,ridge:.3); Near(a.ExpectedOceanAreaKm2,b.ExpectedOceanAreaKm2); Near(a.ExpectedOceanAgeMomentKm3Myr,b.ExpectedOceanAgeMomentKm3Myr); });
Check("rotated crooked material and motion preserve inventories", () => { var a=Open(mesh); var b=Open(Mesh(angle:.37),angle:.37); Near(a.ExpectedOceanAreaKm2,b.ExpectedOceanAreaKm2); foreach(var v in a.Volumes)Near(v.InitialKm3,b.Volumes.Single(w=>w.OriginId==v.OriginId).ProjectedKm3); });
Check("periodic seam-crossing changes no source inventory", () => { var a=Open(mesh); var b=Open(Mesh(offset:400000)); Near(a.ExpectedOceanAreaKm2,b.ExpectedOceanAreaKm2); Near(a.Raster.ContinentalKm.Sum(),b.Raster.ContinentalKm.Sum()); });
Check("nonperiodic cropping is refused", () => { var m=Mesh(offset:400000); Refuse(()=>m.Open(m.At(2),Loading(),scale,32,.01,7,periodic:false)); });
Check("resolution changes preserve continuous component volumes", () => { var a=Open(mesh,16); var b=Open(mesh,64); Near(a.Raster.ContinentalKm.Sum()*a.Raster.CellAreaKm2,b.Raster.ContinentalKm.Sum()*b.Raster.CellAreaKm2); Near(a.Raster.OceanAgeMomentKmMyr.Sum()*a.Raster.CellAreaKm2,b.Raster.OceanAgeMomentKmMyr.Sum()*b.Raster.CellAreaKm2); });
Check("physical area and volume scale with the square of the unit metric", () => { var a=Open(mesh); var b=Open(mesh,km:.02); Near(b.ExpectedOceanAreaKm2,4*a.ExpectedOceanAreaKm2); Near(b.Volumes.Sum(v=>v.ProjectedKm3),4*a.Volumes.Sum(v=>v.ProjectedKm3)); });
Check("smaller game maps retain the same complete reference atlas", () => { var a=Open(mesh); foreach(long size in new long[]{131072,262144}) { var b=mesh.Open(snapshot,Loading(),new TectonicScalePlan(size,size),32,.01,7); True(a.Raster.Checksum==b.Raster.Checksum); } });
Check("snapshots remain immutable after later topology queries", () => { var s=mesh.At(1); string hash=s.Checksum; _=mesh.At(2); True(hash==s.Checksum&&s.Fragments.Count==1); });
Check("snapshot from another mesh cannot control a separation", () => Refuse(()=>Mesh(false).Open(snapshot,Loading(),scale,32,.01,7)));
Check("a missing post-breakup interval is refused", () => Refuse(()=>mesh.Open(snapshot,[new(40,3,12,-1500,0,2500,0)],scale,32,.01,7)));
Check("a disconnected or simultaneous third fragment is not silently assigned", () => { var i=Input(); var e=i.E.Concat(Enumerable.Range(0,6).Select(z=>new MaterialLinkFailure(z*5+1,(z+1)*5+1,2,"second-seam-"+z))).ToArray(); var m=new MaterialCutTopology(i.N,i.F,e); True(m.At(2).Fragments.Count==3); Refuse(()=>m.Open(m.At(2),Loading(),scale,32,.01,7)); });
Check("enclosed fragment cut needs separate contact mechanics", () => { var i=Input(); var f=i.F[18]; var e=new[]{new MaterialLinkFailure(f.A,f.B,2,"ring-a"),new(f.B,f.C,2,"ring-b"),new(f.C,f.A,2,"ring-c")}; var m=new MaterialCutTopology(i.N,i.F,e); True(m.At(2).Fragments.Count==2); Refuse(()=>m.Open(m.At(2),Loading(),scale,32,.01,7)); });
Check("unknown external cells never get ocean or a birth date", () => { var r=Open(mesh).Raster; int count=0; for(int i=0;i<r.ContinentalKm.Count;i++) if(r.ContinentalFraction[i]+r.OceanFraction[i]==0){count++;True(r.OceanicKm[i]==0&&r.OceanAgeMomentKmMyr[i]==0);} True(count>0); });
Check("arbitrary overlapping resolved triangles are refused", () => { var a=Open(mesh).Sources.First(); Refuse(()=>RiftMaterialRaster.ProjectTriangles([a,a with{Id="overlap"}],scale,32,12,.01)); });
Check("subdivision of unchanged motion preserves fields without fake area", () => { var a=Open(mesh); var b=mesh.Open(snapshot,[new(40,2,7,-1500,0,2500,0),new(42,7,12,-1500,0,2500,0)],scale,32,.01,7); for(int i=0;i<a.Raster.OceanicKm.Count;i++){Near(a.Raster.OceanicKm[i],b.Raster.OceanicKm[i]);Near(a.Raster.OceanAgeMomentKmMyr[i],b.Raster.OceanAgeMomentKmMyr[i]);} });
Check("zero or nonfinite metric and motion are refused", () => { Refuse(()=>Open(mesh,km:0)); Refuse(()=>mesh.Open(snapshot,[new(40,2,12,double.NaN,0,1,0)],scale,32,.01,7)); });
var report=new{status=errors.Count==0?"PASS_TOPOLOGY_AND_PRESCRIBED_OPENING":"FAIL",passed=names.Count,failed=errors.Count,names,errors,
    algorithm=MaterialCutTopology.AlgorithmId,automaticFractureLocalization="NOT_IMPLEMENTED",worldHeightChange="NONE",erosion="NOT_RUN"};
Console.WriteLine(JsonSerializer.Serialize(report,json));
if(errors.Count>0)return 1;
if(args.Length==0)return 0;
string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);Write("INCOMPLETE.json",new{status="INCOMPLETE"});
try
{
    foreach(string fixture in new[]{"straight","crooked-pause","periodic"})
    {
        var m=Mesh(crooked:fixture!="straight",offset:fixture=="periodic"?400000:0);
        var r=Open(m,512,pause:fixture=="crooked-pause");
        Directory.CreateDirectory(Path.Combine(root,fixture));var fields=new Dictionary<string,object>();
        Save("continental-fraction",r.Raster.ContinentalFraction,"area fraction");Save("ocean-fraction",r.Raster.OceanFraction,"area fraction");
        Save("continental-thickness",r.Raster.ContinentalKm,"equivalent continental km");Save("oceanic-thickness",r.Raster.OceanicKm,"equivalent oceanic km");
        Save("age-moment",r.Raster.OceanAgeMomentKmMyr,"equivalent oceanic km times model Myr");
        Write(fixture+"/geometry.json",new{m.Nodes,m.Faces,m.Failures,phases=Loading(fixture=="crooked-pause"),snapshots=new[]{m.At(0),m.At(1.5),m.At(2)},
            r.Sources,r.Volumes,r.ExpectedOceanAreaKm2,r.ExpectedOceanAgeMomentKm3Myr,r.Scope});
        Write(fixture+"/packets.json",new{r.Raster.Patches,r.Raster.Packets});
        Write(fixture+"/manifest.json",new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"LOCAL",fixture,width=512,height=512,
            r.Raster.WidthReference,r.Raster.LengthReference,r.Raster.ReferenceKmPerUnit,r.Raster.ObservationTimeMyr,
            r.Raster.Checksum,fields,altitudePresent=false,worldGenerated=false,scope=r.Scope,
            unknownPolicy="outside the supplied material domain stays unknown",nativeResolution="512-square projection; 48 material triangles; 6 supplied failure links"});
        void Save(string name,IReadOnlyList<double> values,string units)
        { byte[] b=new byte[values.Count*8];for(int i=0;i<values.Count;i++)BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(i*8,8),values[i]);
          using(var file=new FileStream(Path.Combine(root,fixture,name+".f64le"),FileMode.CreateNew))file.Write(b);
          fields.Add(name,new{path=name+".f64le",units,sha256=Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant()}); }
    }
    Write("COMPLETE.json",new{status="TOPOLOGY_ONLY_NOT_WORLD_RELIEF",report});File.Delete(Path.Combine(root,"INCOMPLETE.json"));return 0;
}
catch(Exception e){Write("FAILED.json",new{error=e.ToString()});Console.Error.WriteLine(e);return 1;}
void Write(string name,object value){using var f=new FileStream(Path.Combine(root,name),FileMode.CreateNew);JsonSerializer.Serialize(f,value,json);}
