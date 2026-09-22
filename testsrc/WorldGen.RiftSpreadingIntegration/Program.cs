using System.Globalization;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var checks = new List<object>();
var witnesses = new List<object>();
int failures = 0;
double maximumError = 0;
RiftRibbon[] ribbons = [new(10,100000,35,4,5), new(20,50000,28,1,3), new(30,150000,40,6,6)];
RiftLoadingPhase[] paused = [new(0,40,-1500,2500), new(40,50,1000,1000), new(50,60,-500,1500)];
RiftLoadingPhase[] changing = [new(0,40,-1500,2500), new(40,60,-500,1500)];
double[] angles = [0, .37, 1.1, 2.7];
RiftNecking Model(RiftLoadingPhase[] phases, double fraction=.5) => new(ribbons, phases, 300000, 500000, .01, 7, fraction);

Case("interior dates follow both material flanks through pause and velocity change", () => {
    foreach (double angle in angles) foreach (double fraction in new[]{.2,.5,.8}) {
        var rift=Model(paused,fraction); var timelines=Timelines(rift,angle);
        foreach(var e in rift.SpreadingPhases().Where(e=>e.CreatesOcean))
        foreach(double portion in new[]{.1,.3,.7,.9}) foreach(double along in new[]{.1,.43,.9})
        for(int flank=0;flank<2;flank++)
            CheckWitness(rift,timelines,angle,flank,e.StartMyr+portion*(e.EndMyr-e.StartMyr),along,e.EventId);
    }
});
Case("first and final birth endpoints survive rotated frames", () => {
    foreach(double angle in angles) foreach(double fraction in new[]{.2,.5,.8}) {
        var rift=Model([new(0,60,-1500,2500)],fraction); var t=Timelines(rift,angle);
        foreach(double birth in new[]{rift.BreakupTimeMyr!.Value,60d})
        foreach(double along in new[]{0d,.43,1d}) for(int flank=0;flank<2;flank++)
            CheckWitness(rift,t,angle,flank,birth,along,0);
    }
});
Case("active temporal junction has one exact date and younger event", () => {
    foreach(double angle in angles) foreach(double fraction in new[]{.2,.5,.8}) {
        var rift=Model(changing,fraction); var t=Timelines(rift,angle);
        foreach(double along in new[]{.1,.43,.9}) for(int flank=0;flank<2;flank++)
            CheckWitness(rift,t,angle,flank,40,along,1);
    }
});
Case("terminal dormancy retains extinct ridge boundary and never resets its date", () => {
    foreach(double angle in angles) {
        var rift=Model([new(0,40,-1500,2500),new(40,60,1000,1000)]); var t=Timelines(rift,angle);
        for(int flank=0;flank<2;flank++) CheckWitness(rift,t,angle,flank,40,.43,0);
    }
});
Case("points outside the formed segment or ocean gap are not assigned invented ages", () => {
    foreach(double angle in angles) {
        var rift=Model(changing); var t=Timelines(rift,angle); var s=rift.Sample(60);
        foreach(double normal in new[]{s.GapLeftReference-1,s.GapRightReference+1}) {
            var p=Point(normal,.43,rift,angle);
            Require(t.All(v=>!v.TryResolveBirth(p.X,p.Z,out _)),"Outside gap obtained a source");
        }
        foreach(double along in new[]{-1e-4,1.0001}) {
            var p=Particle(rift,angle,0,36,along);
            Require(!t[0].TryResolveBirth(p.X,p.Z,out _),"Beyond ridge endpoint obtained a source");
        }
    }
});
Case("conjugate swept areas equal the created ocean area", () => {
    foreach(double fraction in new[]{.2,.5,.8}) {
        var rift=Model(paused,fraction);double area=0;
        foreach(var p in rift.SpreadingPhases().Where(p=>p.CreatesOcean)) {
            area+=(p.RightMaterialVelocity-p.RidgeVelocity+p.RidgeVelocity-p.LeftMaterialVelocity)
                *(p.EndMyr-p.StartMyr)*rift.AlongRiftLengthReference*.01*.01;
        }
        Near(area,rift.Sample(60).NewOceanAreaKm2,1e-8);
    }
});
Case("aborted rift and zero-area endpoint create no chronology", () => {
    Require(Timelines(Model([new(0,20,-1500,2500)]),0).Length==0,"Subcritical source");
    double breakup=Model([new(0,60,-1500,2500)]).BreakupTimeMyr!.Value;
    Require(Timelines(Model([new(0,breakup,-1500,2500)]),0).Length==0,"Zero-area birth interval");
});
Case("finite orthonormal frame and nonempty identity are enforced", () => {
    var r=Model(paused);
    Refuse(()=>RiftSpreadingAdapter.ToTimelines(r,"invalid",0,0,2,0));
    Refuse(()=>RiftSpreadingAdapter.ToTimelines(r,"invalid",double.NaN,0,1,0));
    Refuse(()=>RiftSpreadingAdapter.ToTimelines(r," ",0,0,1,0));
});
Case("source provenance binds the actual rift and distinguishes conjugate flanks", () => {
    var r=Model(paused);var t=Timelines(r,.37);
    Require(t.Length==2 && t[0].Name!=t[1].Name,"Missing flank identity");
    Require(t.All(v=>v.Name.Contains(r.Checksum,StringComparison.Ordinal)),"Missing material lineage");
    Require(t.All(v=>v.Phases.Count==3 && !v.Phases[1].CreatesOcean),"Dormancy lost");
});
Case("concurrent birth queries reproduce interior ages", () => {
    var r=Model(paused);var t=Timelines(r,.37);var p=Particle(r,.37,0,37,.43);
    Require(t[0].TryResolveBirth(p.X,p.Z,out var expected),"Missing interior source");
    Parallel.For(0,128,_=>{Require(t[0].TryResolveBirth(p.X,p.Z,out var w),"Read failed");
        Require(w==expected,"Mutable reconstruction state");});
});
Console.WriteLine(JsonSerializer.Serialize(new {status=failures==0?"PASS_RIFT_CHRONOLOGY_INTEGRATION":"FAIL",
    riftAlgorithm=RiftNecking.AlgorithmId,spreadingAlgorithm=SpreadingKinematics.AlgorithmId,
    checks,failures,particles=witnesses.Count,maximumBirthErrorMyr=maximumError,witnesses,
    scope="CONTROLLED_TRANSECT_NOT_GLOBAL_WORLD",geographicAcceptance="NOT_EVALUATED",erosion="NOT_RUN"},
    new JsonSerializerOptions{WriteIndented=true}));
return failures==0?0:1;

SpreadingTimeline[] Timelines(RiftNecking r,double angle) => RiftSpreadingAdapter.ToTimelines(r,"rift-integration",12345,-76543,Math.Cos(angle),Math.Sin(angle));
(double X,double Z) Point(double normal,double along,RiftNecking r,double angle) {
    double nx=Math.Cos(angle),nz=Math.Sin(angle),tangent=along*r.AlongRiftLengthReference;
    return(12345+nx*normal-nz*tangent,-76543+nz*normal+nx*tangent);
}
(double X,double Z) Particle(RiftNecking r,double angle,int flank,double birth,double along) {
    // Independent FORWARD trajectory. It never reads the inverse episode or its offsets.
    double normal=r.Sample(birth).RidgeReference!.Value;
    foreach(var p in r.Loading) {
        double dt=Math.Max(0,Math.Min(r.ObservationTimeMyr,p.EndMyr)-Math.Max(birth,p.StartMyr));
        normal+=dt*(flank==0?p.LeftVelocity:p.RightVelocity);
    }
    return Point(normal,along,r,angle);
}
void CheckWitness(RiftNecking r,SpreadingTimeline[] t,double angle,int flank,double birth,double along,int expectedEvent) {
    var p=Particle(r,angle,flank,birth,along);
    if(!t[flank].TryResolveBirth(p.X,p.Z,out var w))
        throw new InvalidOperationException($"Source lost: angle={angle:R} flank={flank} birth={birth:R} along={along:R} x={p.X:R} z={p.Z:R}");
    double error=Math.Abs(w.BirthTimeMyr-(birth-r.ObservationTimeMyr));
    Near(error,0,1e-9);Require(w.SourceEventId==expectedEvent,$"Wrong event {w.SourceEventId}, expected {expectedEvent}");
    maximumError=Math.Max(maximumError,error);
    witnesses.Add(new {angle,flank,birth,along,expectedEvent,witness=w});
}
void Case(string name,Action body) {
    try{body();checks.Add(new{name,status="PASS",error=(string?)null});Console.Error.WriteLine("PASS: "+name);}
    catch(Exception ex){failures++;checks.Add(new{name,status="FAIL",error=ex.ToString()});Console.Error.WriteLine("FAIL: "+name+" "+ex.Message);}
}
static void Near(double a,double b,double tolerance) {Require(double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=tolerance,$"Numeric mismatch {a:R} / {b:R}");}
static void Require(bool v,string message){if(!v)throw new InvalidOperationException(message);}
static void Refuse(Action body){try{body();}catch(ArgumentException){return;}throw new InvalidOperationException("Invalid input accepted");}
