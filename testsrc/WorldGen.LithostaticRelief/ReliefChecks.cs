using ISRWorldGen.Core.Geology.Evolution;
using System.Text.Json;

internal static class ReliefChecks
{
    internal static object Run()
    {
        var checks=new List<string>();var columns=new List<object>();
        void Check(string name,Action a){a();checks.Add(name);Console.Error.WriteLine("PASS "+name);}
        void Near(double a,double b,double tol=1e-9){if(!double.IsFinite(a)||Math.Abs(a-b)>tol)throw new ArithmeticException($"{a:R} != {b:R}");}
        void Require(bool b){if(!b)throw new ArithmeticException("Invariant failure");}
        void Throws(Action a){try{a();}catch(ArgumentException){return;}throw new Exception("Invalid input accepted");}
        Check("zero-age cooling has zero contraction",()=>Near(LithostaticRelief.CoolingContractionKm(0),0,0));
        Check("old ocean approaches a finite thermal thickness",()=>Near(LithostaticRelief.CoolingContractionKm(1000000),2.394,1e-13));
        Check("cooling monotonically subsides",()=>{double old=-1;for(int i=0;i<=400;i++){double d=LithostaticRelief.CoolingContractionKm(i*.5);Require(d>=old);old=d;}});
        Check("short-time cooling matches integrated half-space",()=>Near(LithostaticRelief.CoolingContractionKm(.1),2*3e-5*1330*Math.Sqrt(31.5576*.1/Math.PI),1e-13));
        Check("thermal branch has no numerical discontinuity",()=>{double t=.005*120*120/31.5576;Near(LithostaticRelief.CoolingContractionKm(t*(1-1e-10)),LithostaticRelief.CoolingContractionKm(t*(1+1e-10)),1e-9);});
        Check("air water and mixed columns have identical compensation mass",()=>{
            foreach(var (c,o) in new[]{(35d,0d),(0d,7d),(18d,3d),(80d,4d)})
            foreach(double age in new[]{0d,.1,10,50,150,300})
            {var a=LithostaticRelief.Column(c,o,age);Near(a.ColumnMassEquivalentKm,295.05,1e-10);columns.Add(new{c,o,age,a});}
        });
        Check("unchanged continents do not acquire ocean cooling",()=>Near(LithostaticRelief.Column(35,0,0).ElevationKm,LithostaticRelief.Column(35,0,300).ElevationKm,0));
        Check("young basalt sits above old basalt without water masking",()=>Require(LithostaticRelief.Column(0,7,0).ElevationKm>LithostaticRelief.Column(0,7,100).ElevationKm&&LithostaticRelief.Column(0,7,0).ElevationKm<0));
        Check("no-gravity legacy columns reproduce baseline response",()=>{foreach(double c in new[]{0d,10,35,70})foreach(double age in new[]{0d,50,100})Near(LithostaticRelief.Column(c,7,age,false).ElevationKm,CrustResponse.ElevationKm(c,7,age),1e-12);});
        Check("invalid thermomechanical inputs rejected",()=>{Throws(()=>LithostaticRelief.Column(-1,7,10));Throws(()=>LithostaticRelief.Column(0,0,10));Throws(()=>LithostaticRelief.CoolingContractionKm(double.NaN));Throws(()=>new LithostaticReliefOptions(-1));});
        const int n=8;var p=Enumerable.Range(0,n*n).Select(i=>20d+(i%n==3?4:0)+Math.Sin(i/n*Math.Tau/n)).ToArray();
        Check("uniform potential causes no gravity force",()=>{var f=LithostaticRelief.Forcing(Enumerable.Repeat(50d,n*n).ToArray(),n,2,3,50000);Require(f.East.All(v=>v==0)&&f.South.All(v=>v==0));});
        Check("periodic force has zero net horizontal load",()=>{var f=LithostaticRelief.Forcing(p,n,2,3,50000);Near(f.East.Sum(),0,1e-8);Near(f.South.Sum(),0,1e-8);});
        Check("constant pressure reference does not move material",()=>{var a=LithostaticRelief.Forcing(p,n,2,3,1);var b=LithostaticRelief.Forcing(p.Select(v=>v+100).ToArray(),n,2,3,1);for(int i=0;i<p.Length;i++){Near(a.East[i],b.East[i],1e-13);Near(a.South[i],b.South[i],1e-13);}});
        Check("pressure force points down the potential",()=>{var f=LithostaticRelief.Forcing(p,n,2,3,1);for(int i=0;i<p.Length;i++)Require(f.East[i]*(p[i/n*n+(i%n+1)%n]-p[i])<=0);});
        Check("zero mobility preserves zero body force",()=>{var f=LithostaticRelief.Forcing(p,n,2,3,0);Require(f.East.All(v=>v==0)&&f.South.All(v=>v==0));});
        Check("rectangular metric preserves physical directional gradients",()=>{var a=LithostaticRelief.Forcing(p,n,2,3,1);var b=LithostaticRelief.Forcing(p,n,4,6,2);for(int i=0;i<p.Length;i++){Near(a.East[i],b.East[i],0);Near(a.South[i],b.South[i],0);}});
        Check("optional gravity does not change historical serialized options",()=>Require(!JsonSerializer.Serialize(new StrainWeakeningOptions()).Contains("Gravity")));
        var scale=new TectonicScalePlan(1000000,1000000);
        var settings=new TectonicEvolutionSettings(side:32,duration:2);
        var initial=TectonicHistory.Generate(73,scale,new(side:32,duration:0));
        var assembly=ContinentalAssemblage.Generate(73,scale,32,CrustTransport.Sum(initial.Initial.ContinentalKm)/1024);
        var control=LithostaticReliefWorld.Generate(73,scale,settings,assembly,new(0,false),16);
        var pressure=LithostaticReliefWorld.Generate(73,scale,settings,assembly,new(),16);
        Check("world entrypoint actually changes material transport",()=>Require(control.MechanicalHistory.History.Final.ContinentalKm.Zip(pressure.MechanicalHistory.History.Final.ContinentalKm).Any(v=>Math.Abs(v.First-v.Second)>1e-7)));
        Check("paired worlds preserve every continental origin",()=>{
            foreach(var world in new[]{control,pressure})
            {var h=world.MechanicalHistory.History;for(int i=0;i<h.Plates.Count;i++)Near(h.InitialContinentalByOrigin[i],h.FinalContinentalByOrigin[i],1e-7);}
        });
        Check("world pressure solves retain actual force convergence",()=>Require(pressure.MechanicalHistory.Solves.All(v=>v.RelativeResidual<1.1e-12)));
        Check("whole atlas resizing does not crop a continent",()=>{foreach(long size in new[]{131072L,262144L,1000000L})Near(pressure.SampleElevationKm(new(size,size),size*.37,size*.62),pressure.SampleElevationKm(scale,370000,620000),1e-12);});
        Check("ocean inventory ledger remains balanced",()=>{var l=pressure.MechanicalHistory.History.Ledger;Near((l[^1].OceanicVolume-l[0].OceanicVolume)-(l[^1].CreatedOceanicVolume-l[^1].RecycledOceanicVolume),0,.01);});
        return new{status="NUMERICS_ONLY",passed=checks.Count,checks,columns,
            forceFixture=new{side=n,dx=2,dz=3,potential=p,forcing=LithostaticRelief.Forcing(p,n,2,3,50000)},
            initialMaterial=control.MechanicalHistory.History.InitialMaterialChecksum,
            pairedInitial=pressure.MechanicalHistory.History.InitialMaterialChecksum,
            geographicAcceptance="NOT_ACCEPTED",erosion="NOT_RUN"};
    }
}
