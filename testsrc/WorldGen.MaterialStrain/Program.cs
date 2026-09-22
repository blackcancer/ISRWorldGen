using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

var json=new JsonSerializerOptions { WriteIndented=true };
var checks=new List<string>(); var matrixCases=new List<object>();
void Require(bool condition,string message) { if (!condition) throw new InvalidOperationException(message); }
void Near(double a,double b,double tol=1e-10) { Require(Math.Abs(a-b)<=tol,$"{a:R} != {b:R}"); }
void Refuse(Action a) { try { a(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid operation accepted."); }
void Case(string label,Action a) { a();checks.Add(label);Console.Error.WriteLine("CHECK PASS: "+label); }
Deformation2D I=Deformation2D.Identity;
Case("zero motion preserves the material frame",()=>Require(I.Advance(new(0,0,0,0),7)==I,"zero changed F"));
Case("rigid rotation never becomes thinning",()=> {
 var f=I.Advance(new(0,-.3,.3,0),10);Near(f.XX,Math.Cos(3));Near(f.ZX,Math.Sin(3));Near(f.AreaRatio,1);
 var p=f.Principal();Near(p.Major,1);Near(p.Minor,1);Require(!p.HasDirection,"arbitrary rotation direction");
});
Case("finite simple shear has stretch but no area opening",()=> {
 var f=I.Advance(new(0,2,0,0),3);Near(f.XZ,6);Near(f.AreaRatio,1);Require(f.Principal().Major>6,"missing shear stretch");
});
Case("plane strain matches incompressible crust thinning",()=> {
 var f=I.Advance(new(.08,0,0,0),10);Near(f.AreaRatio,Math.Exp(.8));Near(35/f.AreaRatio,35*Math.Exp(-.8));
 Near(35/f.AreaRatio*f.AreaRatio,35);var p=f.Principal();Near(p.NormalX,1);Near(p.NormalZ,0);
});
Case("extension followed by its inverse is not permanent opening",()=> {
 var f=I.Advance(new(.04,0,0,-.01),10).Advance(new(-.04,0,0,.01),10);Near(f.XX,1);Near(f.ZZ,1);Near(f.AreaRatio,1);
});
Case("isotropic dilation has no privileged rift normal",()=> {
 var p=I.Advance(new(.07,0,0,.07),3).Principal();Require(!p.HasDirection,"fabricated grid normal");
});
Case("rotated extension rotates the reported axis",()=> {
 double c=Math.Cos(.63),s=Math.Sin(.63);var q=new Deformation2D(c,-s,s,c);
 var l=q*new Deformation2D(.1,0,0,0)*new Deformation2D(c,s,-s,c);var f=I.Advance(l,8);var p=f.Principal();
 Near(f.AreaRatio,Math.Exp(.8));Near(Math.Abs(p.NormalX*c+p.NormalZ*s),1);
});
Case("noncommuting rotations and stretching retain path order",()=> {
 var stretch=new Deformation2D(.1,0,0,0);var rotation=new Deformation2D(0,-.1,.1,0);
 var a=I.Advance(stretch,4).Advance(rotation,4);var b=I.Advance(rotation,4).Advance(stretch,4);
 Require(Math.Abs(a.XZ-b.XZ)>.1,"path history lost");Near(a.AreaRatio,b.AreaRatio);
});
Case("subdivision converges to same homogeneous finite deformation",()=> {
 var l=new Deformation2D(.02,-.03,.04,-.01);var a=I.Advance(l,12);var b=I;
 for(int k=0;k<120;k++) b=b.Advance(l,.1);
 Near(a.XX,b.XX);Near(a.XZ,b.XZ);Near(a.ZX,b.ZX);Near(a.ZZ,b.ZZ);
});
Case("record actual C sharp matrix results for independent exponential oracle",()=> {
 for(int k=0;k<24;k++) { var l=new Deformation2D(.06*Math.Sin(k),.13*Math.Cos(k*.3),-.08*Math.Sin(k*.7),.03*Math.Cos(k));
  double dt=.25+k*.17;var f=I.Advance(l,dt);matrixCases.Add(new { gradient=l,dt,result=f });
  Near(f.AreaRatio,Math.Exp(dt*(l.XX+l.ZZ))); }
});
Case("invalid deformation frames and timesteps are refused",()=> {
 Refuse(()=>new Deformation2D(1,0,0,-1).Advance(I,1));Refuse(()=>I.Advance(new(double.NaN,0,0,0),1));
 Refuse(()=>I.Advance(I,-1));Refuse(()=>I.Advance(I,65));
});
MaterialStrainTracker Uniform(int n=32,double width=32,double length=32)=>new(n,width,length,
 Enumerable.Repeat(35d,n*n).ToArray(),new double[n*n],new MaterialStrainOptions(16));
Case("uniform motion moves markers across periodic world boundaries",()=> {
 var t=Uniform();t.Advance(0,2,Enumerable.Repeat(1d,1024).ToArray(),Enumerable.Repeat(-.5,1024).ToArray());
 foreach(var p in t.Snapshot().Samples) { Near(p.CurrentX,(p.InitialX+2)%32);Near(p.CurrentZ,(p.InitialZ-1+32)%32);Near(p.AreaRatio,1);Require(!p.ContinentalThinningCandidate,"translation ruptured"); }
});
Case("invalid observation is atomic and cannot skip a time interval",()=> {
 var t=Uniform();string before=JsonSerializer.Serialize(t.Snapshot());var a=new double[1024];a[2]=double.NaN;
 Refuse(()=>t.Advance(0,1,a,new double[1024]));Refuse(()=>t.Advance(1,1,new double[1024],new double[1024]));
 Refuse(()=>t.Advance(0,1000,Enumerable.Repeat(1d,1024).ToArray(),new double[1024]));
 Require(before==JsonSerializer.Serialize(t.Snapshot()),"failed observation mutated state");
});
Case("published snapshots do not change when tracker advances",()=> {
 var t=Uniform();var before=t.Snapshot();string copy=JsonSerializer.Serialize(before);
 t.Advance(0,1,Enumerable.Repeat(1d,1024).ToArray(),new double[1024]);Require(copy==JsonSerializer.Serialize(before),"snapshot mutated");
});
Case("rectangular metric and uniform rescaling preserve dimensionless strain",()=> {
 var a=Uniform(32,32,64);var b=Uniform(32,32000,64000);
 double[] u=Enumerable.Range(0,1024).Select(i=>.2*Math.Sin(2*Math.PI*(i%32+.5)/32)).ToArray();
 a.Advance(0,2,u,new double[1024]);b.Advance(0,2,u.Select(v=>1000*v).ToArray(),new double[1024]);
 foreach(var pair in a.Snapshot().Samples.Zip(b.Snapshot().Samples)) {Near(pair.First.CurrentX*1000,pair.Second.CurrentX,1e-8);Near(pair.First.AreaRatio,pair.Second.AreaRatio);}
});
Case("periodic simple shear never reports advective thinning",()=> {
 var t=Uniform();double[] u=Enumerable.Range(0,1024).Select(i=>.2*Math.Sin(2*Math.PI*(i/32+.5)/32)).ToArray();
 for(int k=0;k<8;k++)t.Advance(k,1,u,new double[1024]);
 foreach(var p in t.Snapshot().Samples){Near(p.AreaRatio,1);Require(!p.ContinentalThinningCandidate,"shear made ocean opening");}
});
Case("smooth flow trajectories agree with a direct continuous solution",()=> {
 const int n=128;var t=new MaterialStrainTracker(n,128,128,Enumerable.Repeat(35d,n*n).ToArray(),new double[n*n],new MaterialStrainOptions(16,1.01));
 double wave=2*Math.PI/128,amplitude=.4,duration=10;
 double[] u=Enumerable.Range(0,n*n).Select(i=>amplitude*Math.Sin(wave*(i%n+.5))).ToArray();
 for(int k=0;k<100;k++)t.Advance(t.TimeMyr,.1,u,new double[n*n]);
 foreach(var p in t.Snapshot().Samples){double q=Math.Tan(wave*p.InitialX/2),e=Math.Exp(amplitude*wave*duration);
  double pos=2*Math.Atan(e*q)/wave;if(pos<0)pos+=128;
  Near(p.CurrentX,pos,.002);Near(p.AreaRatio,e*(1+q*q)/(1+e*e*q*q),.006);}
});
Case("oceanic markers cannot become continental rift candidates",()=> {
 var t=new MaterialStrainTracker(32,32,32,new double[1024],Enumerable.Repeat(7d,1024).ToArray(),new MaterialStrainOptions(16,1.001));
 double[] u=Enumerable.Range(0,1024).Select(i=>.3*Math.Sin(2*Math.PI*(i%32+.5)/32)).ToArray();t.Advance(0,4,u,new double[1024]);
 Require(t.Snapshot().Samples.Any(p=>p.AreaRatio>1.01),"no extensional example");Require(!t.Snapshot().Samples.Any(p=>p.ContinentalThinningCandidate),"ocean classified continent");
});
Case("material geometry and thresholds reject nonfinite or unsupported data",()=> {
 Refuse(()=>new MaterialStrainTracker(32,0,32,new double[1024],new double[1024],new()));
 Refuse(()=>new MaterialStrainTracker(32,32,32,new double[1024],new double[1024],new(16)));
 Refuse(()=>new MaterialStrainTracker(32,32,32,Enumerable.Repeat(35d,1024).ToArray(),new double[1024],new(64)));
 Refuse(()=>new MaterialStrainTracker(32,32,32,Enumerable.Repeat(35d,1024).ToArray(),new double[1024],new(16,1)));
});
Case("passive observations preserve actual Core history and material checksums",()=> {
 var scale=new TectonicScalePlan(1000000,1000000);var settings=new TectonicEvolutionSettings(side:64,duration:3);
 var a=ContinentalAssemblage.Generate(73,scale,64,12);
 var baseline=MaterialBoundHistory.GenerateWithAssemblage(73,scale,settings,a);
 var observed=MaterialBoundHistory.GenerateWithStrain(73,scale,settings,a,new(16));
 Require(baseline.Checksum==observed.History.Checksum,"observer changed Core identity");
 Require(baseline.FinalMaterialChecksum==observed.History.FinalMaterialChecksum,"observer changed material");
 Require(baseline.Final.ElevationKm.SequenceEqual(observed.History.Final.ElevationKm),"observer changed heights");
 Near(observed.Strain.TimeMyr,baseline.Final.Time);
});
Case("zero duration leaves all material markers undeformed",()=> {
 var scale=new TectonicScalePlan(131072,131072);var a=ContinentalAssemblage.Generate(73,scale,32,12);
 var observed=MaterialBoundHistory.GenerateWithStrain(73,scale,new(side:32,duration:0),a,new(16));
 Require(observed.Strain.Samples.All(p=>p.F==I && p.FirstAreaThresholdSampleTime==-1),"unrun history has strain");
});
Case("uniform plane strain reproduces each pre-breakup ribbon Jacobian",()=> {
 var rift=new RiftNecking([new(0,100000,35,4,4),new(1,50000,28,1,3),new(2,150000,40,6,4)],
  [new(0,20,-2000,2000)],300000,100000,.01,7);
 var section=rift.Sample(20);
 foreach(var parcel in section.Parcels){var r=rift.Ribbons.Single(v=>v.OriginId==parcel.OriginId);
  double beta=(parcel.RightReference-parcel.LeftReference)/r.WidthReference;
  var f=I.Advance(new(Math.Log(beta)/20,0,0,0),20);Near(r.CrustKm/f.AreaRatio,parcel.CrustKm);}
});

if(args.Length==0){Console.WriteLine(JsonSerializer.Serialize(new { status="PASS_KINEMATICS_ONLY",checks,matrixCases },json));return;}
if(args.Length!=2)throw new ArgumentException("Usage: MaterialStrain NEW_DIRECTORY SEED or no arguments for checks.");
string root=Path.GetFullPath(args[0]);int seed=int.Parse(args[1],CultureInfo.InvariantCulture);
if(seed is not (-437287116 or 20260906 or 73))throw new ArgumentException("Frozen three-seed campaign.");
if(Directory.Exists(root)||File.Exists(root))throw new IOException("Never overwrite prior evidence.");
var fields=new Dictionary<string,object>();
Directory.CreateDirectory(root);Write("INCOMPLETE.json",new { status="INCOMPLETE",seed });
try {
 const int side=512;
 var scale=new TectonicScalePlan(1000000,1000000);var settings=new TectonicEvolutionSettings(side:side);
 var old=TectonicHistory.Generate(seed,scale,new(side:side,duration:0));
 double budget=CrustTransport.Sum(old.Initial.ContinentalKm)/(side*side);
 var assemblage=ContinentalAssemblage.Generate(seed,scale,side,budget);
 var result=MaterialBoundHistory.GenerateWithStrain(seed,scale,settings,assemblage,new(128,2));
 var history=result.History;
 Write("checks.json",new { status="PASS_KINEMATICS_ONLY",checks,matrixCases });
 Write("markers.json",result.Strain);
 Save("initial-height",history.Initial.ElevationKm.Select(v=>168+12*v).ToArray(),"solid Y blocks");
 Save("height",history.Final.ElevationKm.Select(v=>168+12*v).ToArray(),"solid Y blocks");
 Save("elevation-model",history.Final.ElevationKm,"native signed model km");
 foreach(long size in new long[]{131072,262144,1000000})
  Near(history.SampleElevationKm(new(size,size),size*.375,size*.625),history.SampleElevationKm(scale,375000,625000),1e-10);
 Write("manifest.json",new { commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"LOCAL",seed,width=side,height=side,
  worldWidthBlocks=1000000,worldLengthBlocks=1000000,sampleStepBlocks=1000000d/side,
  markerSide=128,markerInitialStepReference=1000000d/128,markersCoordinateSystem="INITIAL_MATERIAL_REFERENCE_WITH_CURRENT_COORDINATES",
  algorithm=MaterialStrainTracker.AlgorithmId,historyAlgorithm=MaterialBoundHistory.AlgorithmId,history.Checksum,history.FinalMaterialChecksum,
  assemblage=assemblage.Checksum,settings,fields,seabedMasked=false,
  geographicAcceptance="NOT_ACCEPTED",erosion="NOT_RUN",physics="PASSIVE_KINEMATICS_NOT_FRACTURE_OR_FORCE_BALANCE" });
 Write("ledger.json",history.Ledger);
 var s=result.Strain.Samples;
 Write("COMPLETE.json",new {status="PASSIVE_OBSERVATION_COMPLETED",seed,checks=checks.Count,
  samples=s.Count,candidates=s.Count(v=>v.ContinentalThinningCandidate),
  continentalMarkers=s.Count(v=>v.InitialContinentalKm>=10 && v.InitialContinentalKm/(v.InitialContinentalKm+v.InitialOceanicKm)>=.8),
  minimumArea=s.Min(v=>v.AreaRatio),maximumArea=s.Max(v=>v.AreaRatio),maximumMajorStretch=s.Max(v=>v.MajorStretch),
  createdRiftEvents=0,geographicAcceptance="NOT_ACCEPTED",erosion="NOT_RUN"});
 File.Delete(Path.Combine(root,"INCOMPLETE.json"));
} catch(Exception e){Write("FAILED.json",new { error=e.ToString(),seed });throw;}
void Write(string name,object value){using var file=new FileStream(Path.Combine(root,name),FileMode.CreateNew);JsonSerializer.Serialize(file,value,json);}
void Save(string name,IReadOnlyList<double> values,string units){
 if(values.Count!=512*512||values.Any(v=>!double.IsFinite(v)))throw new ArithmeticException("Invalid field.");
 byte[] bytes=new byte[values.Count*8];for(int i=0;i<values.Count;i++)BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(8*i,8),values[i]);
 using(var file=new FileStream(Path.Combine(root,name+".f64le"),FileMode.CreateNew))file.Write(bytes);
 fields.Add(name,new {path=name+".f64le",sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),units});
}
