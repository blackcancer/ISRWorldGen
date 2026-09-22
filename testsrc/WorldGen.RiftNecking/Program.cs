using System.Globalization;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

// Pure checks have no persistence or user-world side effects. JSON is printed to
// stdout; the calling campaign owns its unique output path and publication.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var results = new List<string>();
RiftRibbon[] material = [new(10, 100000, 35, 4, 5), new(20, 50000, 28, 1, 3), new(30, 150000, 40, 6, 6)];
RiftLoadingPhase[] load = [new(0, 60, -1500, 2500)];
RiftNecking Make(RiftRibbon[]? m = null, RiftLoadingPhase[]? p = null, double fraction = .5) =>
    new(m ?? material, p ?? load, 300000, 500000, .01, 7, fraction);
var model = Make();
Case("analytic weakest ribbon and critical opening", () => {
    Near(model.CriticalLoadCoordinate, 2d / 3); Near(model.CriticalOpeningReference, 138750);
    Near(model.BreakupTimeMyr!.Value, 34.6875); Require(model.Sample(60).RupturedOriginId == 20); });
Case("all material origins conserve volume at 101 times", () => {
    for (int step = 0; step <= 100; step++) {
        var s = model.Sample(.6 * step); Near(s.ContinentalVolumeKm3, 545000000);
        foreach (var r in material) Near(s.Parcels.Where(p => p.OriginId == r.OriginId)
            .Sum(p => (p.RightReference - p.LeftReference) * p.CrustKm), r.WidthReference * r.CrustKm);
    } });
Case("weak ribbon concentrates necking", () => {
    var s = model.Sample(20); double b0 = 35 / s.Parcels[0].CrustKm, b1 = 28 / s.Parcels[1].CrustKm;
    Require(b1 > b0 && s.NewOceanVolumeKm3 == 0 && s.RupturedOriginId is null); });
Case("subcritical rift never creates ocean sources", () => {
    var shortRift = Make(p: [new(0, 20, -1500, 2500)]);
    Require(shortRift.BreakupTimeMyr is null && shortRift.SpreadingPhases().Count == 0);
    Require(shortRift.Sample(20).NewOceanVolumeKm3 == 0); });
Case("zero opening only translates the material", () => {
    var s = Make(p: [new(0, 60, 800, 800)]).Sample(60);
    Require(s.RidgeReference is null); Near(s.Parcels[0].LeftReference, 348000);
    Near(s.Parcels[0].CrustKm, 35); });
Case("opening gap and mantle source volume agree", () => {
    var s = model.Sample(60); Near(s.GapRightReference - s.GapLeftReference, 101250);
    Near(s.NewOceanAreaKm2, 5062500); Near(s.NewOceanVolumeKm3, 35437500);
    Require(s.RidgeReference > s.GapLeftReference && s.RidgeReference < s.GapRightReference); });
Case("rupture is zero-area and removes no residual continent", () => {
    var s = model.Sample(model.BreakupTimeMyr!.Value); Require(s.Parcels.Count == 4);
    Near(s.GapRightReference - s.GapLeftReference, 0); Near(s.Parcels[1].CrustKm, 28d / 3);
    Near(s.Parcels[1].RightReference, s.Parcels[2].LeftReference); });
Case("no ocean is dated before actual breakup", () => {
    var events = model.SpreadingPhases(); Require(events.Count == 1);
    Near(events[0].StartMyr, 34.6875); Near(events[0].EndMyr, 60);
    Require(events[0].CreatesOcean && events[0].EventId >= 0); });
Case("pause transports existing floor without birthing more", () => {
    var m = Make(p: [new(0,40,-1500,2500),new(40,50,1000,1000),new(50,60,-500,1500)]);
    var e = m.SpreadingPhases(); Require(e.Count == 3 && !e[1].CreatesOcean && e[1].EventId == -1);
    Near(m.Sample(50).NewOceanAreaKm2, m.Sample(40).NewOceanAreaKm2); });
Case("common translation cannot change breakup or thickness", () => {
    var a = model.Sample(60); var b = Make(p: [new(0,60,-1000,3000)]).Sample(60);
    Near(b.BreakupTimeMyr!.Value, a.BreakupTimeMyr!.Value);
    for (int i=0;i<a.Parcels.Count;i++) { Near(b.Parcels[i].LeftReference-a.Parcels[i].LeftReference,30000); Near(a.Parcels[i].CrustKm,b.Parcels[i].CrustKm); } });
Case("time partition cannot manufacture a different source identity", () => {
    var m = Make(p: [new(0,20,-1500,2500),new(20,45,-1500,2500),new(45,60,-1500,2500)]);
    Require(m.Checksum == model.Checksum && m.SpreadingPhases().Count == 1); });
Case("ridge asymmetry redistributes age flanks, not material volume", () => {
    var a = model.Sample(60); var b = Make(fraction:.25).Sample(60);
    Near(a.NewOceanVolumeKm3,b.NewOceanVolumeKm3); Require(a.RidgeReference != b.RidgeReference); });
Case("first strength failure is not selected by material ID", () => {
    var changed = material.Select(r => r with { OriginId = 100-r.OriginId }).ToArray();
    var a = Make(changed).Sample(60); Require(a.RupturedOriginId == 80);
    Near(a.BreakupTimeMyr!.Value,34.6875); });
Case("simultaneous constitutive failures refuse arbitrary topology", () => {
    RiftRibbon[] equal = [new(10,100000,35,1,2),new(20,100000,35,1,2)];
    Refuse(() => Make(equal,[new(0,100,-2000,2000)]));
    var chosen = new RiftNecking(equal,[new(0,100,-2000,2000)],300000,500000,.01,7,
        explicitlySelectedFailureOrigin:20); Require(chosen.Sample(100).RupturedOriginId==20); });
Case("closing and discontinuous histories are rejected", () => {
    Refuse(() => Make(p:[new(0,60,1500,-2500)]));
    Refuse(() => Make(p:[new(0,20,-1,1),new(21,60,-1,1)]));
    Refuse(() => Make(p:[new(0,20,-1,1),new(19,60,-1,1)])); });
Case("invalid constitutive parameters and event identities are refused", () => {
    Refuse(() => Make([material[0] with { Resistance=double.NaN }]));
    Refuse(() => Make([material[0] with { FailureStretch=1 }]));
    Refuse(() => Make([material[0],material[0]])); Refuse(() => Make(fraction:1));
    Refuse(() => new RiftNecking(material,load,0,500000,.01,7,firstSourceEventId:int.MaxValue)); });
Case("samples and output objects cannot mutate inputs", () => {
    var source=material.ToArray();var p=load.ToArray();var m=Make(source,p);string hash=m.Checksum;
    source[1]=source[1] with {Resistance=9};p[0]=new(0,60,0,0);
    Near(m.BreakupTimeMyr!.Value,34.6875);Require(m.Checksum==hash);
    Refuse(() => m.Sample(-1));Refuse(() => m.Sample(61));Refuse(() => m.Sample(double.NaN)); });
Case("parallel reads reproduce the exact same section", () => {
    double value=model.Sample(42).Parcels[1].LeftReference;
    Parallel.For(0,64,_ => Require(model.Sample(42).Parcels[1].LeftReference==value)); });
Console.WriteLine(JsonSerializer.Serialize(new {algorithm=RiftNecking.AlgorithmId,status="PASS_CONTROLLED_MODEL_ONLY",
    checks=results,model.Checksum,final=model.Sample(60),sources=model.SpreadingPhases(),
    geographicAcceptance="NOT_EVALUATED",erosion="NOT_RUN",nativeGame="NOT_RUN"},new JsonSerializerOptions{WriteIndented=true}));
void Case(string name,Action run) {run();results.Add(name);Console.Error.WriteLine("PASS: "+name);}
static void Require(bool b) {if(!b) throw new InvalidOperationException("Rift check failed.");}
static void Near(double a,double b) {Require(double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=2e-10*Math.Max(1,Math.Abs(b)));}
static void Refuse(Action action) {try{action();}catch(ArgumentException){return;}catch(ArithmeticException){return;}throw new InvalidOperationException("Invalid input accepted.");}
