using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

internal static class DrivingChecks
{
    internal static object Run()
    {
        var checks = new List<string>();
        void Check(string name, Action a) { a(); checks.Add(name); Console.Error.WriteLine("PASS " + name); }
        void Require(bool b) { if (!b) throw new ArithmeticException("Invariant failed"); }
        void Near(double a, double b, double tol = 1e-10) { if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Abs(a-b)>tol) throw new ArithmeticException($"{a:R} != {b:R}"); }
        void Throws(Action a) { try { a(); } catch (ArgumentException) { return; } throw new Exception("Invalid input accepted"); }
        TectonicPlate[] plates = [new(0, 100000, 200000, 300, 400), new(1, 700000, 800000, -200, 150)];
        var input = new[] { 1.2, -0.8 };
        var history = new PlateDrivingSchedule(96, input);
        Check("initial load is the exact input, not resampled", () => Require(history.At(0, plates).SequenceEqual(plates)));
        Check("zero turns reproduce the stationary vectors exactly", () => Require(new PlateDrivingSchedule(96, [0,0]).At(47,plates).SequenceEqual(plates)));
        Check("preferred speed is conserved during directional evolution", () => { for(int t=0;t<=96;t++) { var p=history.At(t,plates); for(int i=0;i<2;i++) Near(p[i].Vx*p[i].Vx+p[i].Vz*p[i].Vz,plates[i].Vx*plates[i].Vx+plates[i].Vz*plates[i].Vz,1e-7); } });
        Check("end rotation has its declared angle", () => { var p=history.At(96,plates); Near(p[0].Vx,Math.Cos(1.2)*300-Math.Sin(1.2)*400); });
        Check("middle time uses the documented cubic history", () => Near(history.At(48,plates)[1].Vz,Math.Sin(-.4)*(-200)+Math.Cos(-.4)*150));
        Check("scheduling queries do not depend on call order", () => { var a=history.At(12,plates); _=history.At(80,plates); Require(a.SequenceEqual(history.At(12,plates))); });
        Check("material identities and source sites are never relabelled", () => { var p=history.At(75,plates); for(int i=0;i<2;i++) Require(p[i].Id==plates[i].Id&&p[i].X==plates[i].X&&p[i].Z==plates[i].Z); });
        Check("constructor owns its parameter array", () => { input[0]=0; Near(history.TurnsRadians[0],1.2,0); });
        Check("input velocities remain unmodified", () => { _=history.At(40,plates); Near(plates[0].Vx,300,0); });
        Check("invalid times and loads are rejected", () => { Throws(()=>history.At(-1,plates)); Throws(()=>history.At(double.NaN,plates)); Throws(()=>history.At(97,plates)); Throws(()=>new PlateDrivingSchedule(0,[0,0])); Throws(()=>new PlateDrivingSchedule(96,[double.NaN,0])); });
        Check("missing or unordered origins are rejected", () => { Throws(()=>history.At(1,[plates[1],plates[0]])); Throws(()=>history.At(1,[plates[0]])); });
        var scale = new TectonicScalePlan(1000000,1000000);
        Check("whole-map scaling preserves the driving prior", () => { var a=PlateDrivingSchedule.SpatialPrior(73,scale,plates,96);var b=PlateDrivingSchedule.SpatialPrior(73,new(131072,131072),plates,96); Require(a.TurnsRadians.SequenceEqual(b.TurnsRadians)); });
        Check("zero maximum turn disables directional change", () => Require(PlateDrivingSchedule.SpatialPrior(73,scale,plates,96,0).At(96,plates).SequenceEqual(plates)));
        Check("unrequested schedule does not alter historical option JSON", () => Require(!JsonSerializer.Serialize(new StrainWeakeningOptions()).Contains("Driving")));
        Check("numeric PNG rejects NaN and out of range without clipping", () => { Throws(()=>NumericHeightPng.Encode([double.NaN],1)); Throws(()=>NumericHeightPng.Encode([-1d],1)); Throws(()=>NumericHeightPng.Encode([384d],1)); });
        Check("numeric PNG encodes both seabed and land", () => { var b=NumericHeightPng.Encode([0d,100d,168d,383d],2);Require(b[0]==137&&b[25]==0&&b[24]==16); });
        var cfg = new TectonicEvolutionSettings(side:32,duration:4);
        var init=TectonicHistory.Generate(73,scale,new(side:32,duration:0));
        var assemblage=ContinentalAssemblage.Generate(73,scale,32,CrustTransport.Sum(init.Initial.ContinentalKm)/1024);
        var control=LithostaticReliefWorld.Generate(73,scale,cfg,assemblage,new(),16);
        var zero=LithostaticReliefWorld.Generate(73,scale,cfg,assemblage,new(),16,new(4,Enumerable.Repeat(0d,init.Plates.Count).ToArray()));
        var evolved=LithostaticReliefWorld.Generate(73,scale,cfg,assemblage,new(),16,PlateDrivingSchedule.SpatialPrior(73,scale,init.Plates,4));
        Check("zero-turn complete history preserves exact heights", () => Require(control.ElevationKm.SequenceEqual(zero.ElevationKm)));
        Check("zero-turn complete history preserves material checksum", () => Require(control.MechanicalHistory.History.FinalMaterialChecksum==zero.MechanicalHistory.History.FinalMaterialChecksum));
        Check("paired initial crust is identical", () => Require(control.MechanicalHistory.History.InitialMaterialChecksum==evolved.MechanicalHistory.History.InitialMaterialChecksum));
        Check("evolving driving changes actual transported material", () => Require(control.MechanicalHistory.History.FinalMaterialChecksum!=evolved.MechanicalHistory.History.FinalMaterialChecksum));
        Check("evolving driving changes the computed solid elevation", () => Require(control.ElevationKm.Zip(evolved.ElevationKm).Any(x=>Math.Abs(x.First-x.Second)>1e-5)));
        Check("every continental origin conserves inventory", () => {var h=evolved.MechanicalHistory.History;for(int i=0;i<h.Plates.Count;i++)Near(h.InitialContinentalByOrigin[i],h.FinalContinentalByOrigin[i],1e-7);});
        Check("force balance remains enforced in every solve", () => Require(evolved.MechanicalHistory.Solves.All(s=>s.RelativeResidual<=1.01e-12)));
        Check("multiple homologous samples scale without cropping", () => {foreach(long w in new[]{8192L,131072,262144,1000000})foreach(double u in new[]{.1,.37,.83})foreach(double v in new[]{.02,.61,.95})Near(evolved.SampleElevationKm(new(w,w),w*u,w*v),evolved.SampleElevationKm(scale,1000000*u,1000000*v),1e-11);});
        Check("incomplete load duration is refused before integration", () => Throws(()=>LithostaticReliefWorld.Generate(73,scale,cfg,assemblage,new(),16,new(2,Enumerable.Repeat(0d,init.Plates.Count).ToArray()))));
        var world=IntegratedReliefGenerator.Generate(73,262144,262144,32,1);
        Check("integrated public entrypoint returns actual whole-world heights", () => Require(world.World.ElevationKm.Count==1024&&world.Scale.ReferenceWidth==1000000));
        Check("load receipt corresponds to every actual mechanical solve", () => Require(world.LoadHistory.Select(s=>s.TimeMyr).SequenceEqual(world.World.MechanicalHistory.Solves.Select(s=>s.Time))));
        Check("integrated default entrypoint changes no saved-world setting", () => Require(world.Scale.WidthBlocks==262144&&world.Driving!=null));
        return new { status="PASS_DRIVING_AND_CORE_NOT_GEOGRAPHY",passed=checks.Count,checks,
            fixture=new {plates,angles=history.TurnsRadians,times=Enumerable.Range(0,97).Select(t=>new{time=t,load=history.At(t,plates)})},
            oceanHistory="INHERITED_AGE_PRIOR_UNCHANGED",erosion="NOT_RUN",nativeRuntime="NOT_RUN"};
    }
}
