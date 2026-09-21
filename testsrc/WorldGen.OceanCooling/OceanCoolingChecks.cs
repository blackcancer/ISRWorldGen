using ISRWorldGen.Core.Geology.Evolution;

internal static class OceanCoolingChecks
{
    internal static string[] Run()
    {
        var passed = new List<string>(); var options = new OceanCoolingOptions();
        Case("finite plate has zero cooling at birth and a finite old-age limit", () =>
        { Near(options.ColdFraction(0),0,0); Near(options.ColdFraction(1e8),1,1e-14); });
        Case("cooling is monotonic without a switch at sea level", () =>
        {
            double previous=-1;
            foreach(double age in new[]{0d,.001,.01,.1,1,10,50,100,200,1000})
            { double c=options.ColdFraction(age); Require(c>=previous&&c<=1+1e-14,"cooling reversal");previous=c; }
        });
        Case("truncated thermal spectrum respects the full-series error bound", () =>
        {
            foreach(double age in new[]{.0001,.001,.01,.1,1,10,50,100,200})
            {
                double warm=0;
                for(int k=0;k<4096;k++) {double n=2*k+1;warm+=8/(Math.PI*Math.PI*n*n)*Math.Exp(-n*n*age/options.FundamentalTimeMyr);}
                double truth=1-warm, approx=options.ColdFraction(age);
                Require(approx<=truth+1e-12 && truth-approx<=options.TailWeight+1e-12,"spectrum bound failed");
            }
        });
        Case("spectral error bound decreases with retained modes", () =>
            Require(new OceanCoolingOptions(32).ConservativeElevationErrorKm < new OceanCoolingOptions(4).ConservativeElevationErrorKm,"missing convergence"));
        Case("single anchor datum preserves existing initial columns", () =>
        {
            foreach(var (c,o) in new[]{(0d,7d),(35d,0d),(17d,3.5),(40d,1d)})
                Near(options.ElevationKm(c,o,options.ColdFraction(50)),CrustResponse.ElevationKm(c,o,50),1e-12);
        });
        Case("thermal correction is zero on purely continental columns", () =>
        { Near(options.ElevationKm(35,0,0),options.ElevationKm(35,0,1),0); });
        Case("old seabed does not deepen without limit", () =>
        { Near(options.ElevationKm(0,7,options.ColdFraction(1e5)),options.ElevationKm(0,7,options.ColdFraction(1e8)),1e-12); });
        Case("two birth histories with equal mean ages have distinct thermal states", () =>
        {
            double mixed=.5*(options.ColdFraction(0)+options.ColdFraction(100));
            Require(Math.Abs(mixed-options.ColdFraction(50))>.1,"lost thermal history in an average age");
        });
        Case("invalid physical parameters and columns are refused", () =>
        {
            Reject(()=>new OceanCoolingOptions(retainedOddModes:0));Reject(()=>new OceanCoolingOptions(diffusivityM2PerSecond:double.NaN));
            Reject(()=>options.ColdFraction(-1));Reject(()=>options.ElevationKm(0,0,0));Reject(()=>options.ElevationKm(0,7,2));
        });
        Case("uniform age under translation remains exact after aging", () =>
        {
            var a=State([7,7,7,7],[50,50,50,50]);var t=OceanThermalCohorts.Create(a,options);
            double[] e=[.2,.2,.2,.2],s=new double[4];
            for(int step=0;step<8;step++) {var b=a.Advect(e,s,1,1,.5).Age(.5);t=t.Advance(a,b,e,s,1,1,.5,new double[4],[-1,-1,-1,-1],new double[4]);a=b;t.RequireCarriers(a);}
            foreach(double c in t.ColdFractions())Near(c,options.ColdFraction(54),1e-12);
        });
        Case("mixed donor temperatures are carried instead of reevaluated at mean age", () =>
        {
            var a=State([7,7,7,7],[0,100,0,100]);var t=OceanThermalCohorts.Create(a,options);
            double[] e=[.5,.5,.5,.5],s=new double[4];var b=a.Advect(e,s,1,1,1).Age(1);
            t=t.Advance(a,b,e,s,1,1,1,new double[4],[-1,-1,-1,-1],new double[4]);
            foreach(double c in t.ColdFractions())Near(c,.5*(options.ColdFraction(1)+options.ColdFraction(101)),1e-12);
        });
        Case("new basalt starts hot and thermal mixing follows born volume", () =>
        {
            var a=State([7,7,7,7],[50,50,50,50]);var t=OceanThermalCohorts.Create(a,options);double[] v=new double[4],born=[7,0,0,0];
            t=t.Advance(a,a,v,v,1,1,0,born,[-1,-1,-1,-1],v);
            var b=a.ExchangeOcean(born,[-1,-1,-1,-1],v);t.RequireCarriers(b);
            Near(t.ColdFractions()[0],.5*options.ColdFraction(50),1e-12);
        });
        Case("complete selective recycling removes only the matching thermal origin", () =>
        {
            var a=MaterialPlateCohorts.Create(2,2,[0,1,0,1],[35,35,35,35],[7,7,7,7],[0,700,0,700],[7,7,7,7]);
            var t=OceanThermalCohorts.Create(a,options);double[] v=new double[4],removed=[7,0,0,0];int[] lower=[0,-1,-1,-1];
            t=t.Advance(a,a,v,v,1,1,0,v,lower,removed);t.RequireCarriers(a.ExchangeOcean(v,lower,removed));
            Near(t.ColdFractions()[0],0,0);Near(t.ColdFractions()[1],options.ColdFraction(100),1e-12);
        });
        Case("subnormal ocean packets cannot leave orphan thermal moments", () =>
        {
            var a=State([double.Epsilon,0,0,0],[50,0,0,0]);var t=OceanThermalCohorts.Create(a,options);
            double[] e=[.125,.125,.125,.125],v=new double[4];var b=a.Advect(e,v,1,1,1).Age(1);
            t=t.Advance(a,b,e,v,1,1,1,v,[-1,-1,-1,-1],v);t.RequireCarriers(b);
            Near(t.ColdFractions()[1],0,0);
        });
        Case("outgoing CFL and stale thermal state are refused", () =>
        {
            var a=State([7,7,7,7],[50,50,50,50]);var t=OceanThermalCohorts.Create(a,options);double[] v=new double[4];
            Reject(()=>t.Advance(a,a,[2,2,2,2],v,1,1,1,v,[-1,-1,-1,-1],v));
            var other=State([8,7,7,7],[50,50,50,50]);Reject(()=>t.Advance(other,other,v,v,1,1,0,v,[-1,-1,-1,-1],v));
        });
        Case("one-way thermal coupling preserves actual material and mechanical history", () =>
        {
            var scale=new TectonicScalePlan(1000000,1000000);var settings=new TectonicEvolutionSettings(side:64,duration:2);
            var assemblage=ContinentalAssemblage.Generate(73,scale,64);
            var rheology=new SheetRheologyOptions(mechanicalSide:16);
            var a=MaterialBoundHistory.GenerateWithRheology(73,scale,settings,assemblage,rheology);
            var b=MaterialBoundHistory.GenerateWithOceanCooling(73,scale,settings,assemblage,rheology,options);
            Require(a.FinalMaterialChecksum==b.FinalMaterialChecksum,"thermal experiment changed material transport");
            Require(a.MechanicalSolves.SequenceEqual(b.MechanicalSolves),"thermal experiment changed mechanical forcing");
            Require(b.ThermalReceipts.Count>0&&b.FinalColdFraction is not null,"missing thermal evidence");
            Require(a.Checksum!=b.Checksum,"thermal algorithm missing from history identity");
            foreach(long size in new long[]{131072,262144,1000000})
                Near(b.SampleElevationKm(new TectonicScalePlan(size,size),.25*size,.75*size),b.SampleElevationKm(scale,250000,750000),1e-12);
        });
        return passed.ToArray();
        void Case(string label,Action action){action();passed.Add(label);Console.WriteLine("CHECK PASS: "+label);}
    }
    private static MaterialPlateCohorts State(double[] ocean,double[] ages) => MaterialPlateCohorts.Create(2,1,new int[4],
        [35,35,35,35],ocean,ocean.Select((v,i)=>v*ages[i]).ToArray(),ocean);
    private static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static void Near(double a,double b,double tolerance){Require(double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=tolerance,$"{a:R} != {b:R}");}
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}catch(ArithmeticException){return;}throw new InvalidOperationException("Invalid input was accepted");}
}
