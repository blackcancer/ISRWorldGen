using ISRWorldGen.Core.Geology.Evolution;

internal static class YieldChecks
{
    internal static object Run()
    {
        var names = new List<string>(); var fixtures = new List<object>();
        void Check(string name, Action action) { action(); names.Add(name); Console.Error.WriteLine("CHECK PASS: " + name); }
        Check("subyield stress is ductile and produces no plastic strain", () => {
            var a=ViscoplasticSheet.Response(.1,3,4,.15);Near(a.Stress,.6);Near(a.PlasticRate,0);Near(a.Potential,.06);
        });
        Check("yield onset is continuous in stress and potential", () => {
            var a=ViscoplasticSheet.Response(2d/3,3,4,.15);Near(a.Stress,4);Near(a.PlasticRate,0);Near(a.Potential,8d/3);
            var b=ViscoplasticSheet.Response(2d/3+1e-8,3,4,.15);Near(b.Stress,4+9e-9,1e-12);
        });
        Check("postyield excess and ductile strain reconstruct total rate", () => {
            foreach(double r in new[]{.1,1d,5,100}) { var a=ViscoplasticSheet.Response(r,3,4,.15);Near(a.Stress/(2*3)+a.PlasticRate,r);Require(a.PlasticRate>=0); }
        });
        Check("the regularized overstress is declared not clipped", () => {var a=ViscoplasticSheet.Response(3,2,4,.15);Require(a.Stress>4 && a.Stress<12);Near(a.Tangent,.6);});
        Check("potential derivative is twice conjugate stress", () => {
            foreach(double r in new[]{.2,1d,4d}) {double e=1e-6;var a=ViscoplasticSheet.Response(r,3,4,.15);Near((ViscoplasticSheet.Response(r+e,3,4,.15).Potential-ViscoplasticSheet.Response(r-e,3,4,.15).Potential)/(2*e),2*a.Stress,1e-7);}
        });
        Check("disabled yielding exactly restores the scalar ductile law",()=>{foreach(double r in new[]{0d,1d,100d}){var a=ViscoplasticSheet.Response(r,3,4,1);Near(a.Stress,6*r);Near(a.Potential,6*r*r);Near(a.PlasticRate,0);}});
        Check("invalid material and yield inputs are refused",()=>{Throws(()=>ViscoplasticSheet.Response(-1,3,4,.15));Throws(()=>ViscoplasticSheet.Response(1,double.NaN,4,.15));Throws(()=>new SheetYieldOptions(continentalLoad:0));Throws(()=>new SheetYieldOptions(postYieldRatio:0));});
        const int n=8; double[] mu=Enumerable.Repeat(2d,n*n).ToArray(),load=Enumerable.Repeat(1d,n*n).ToArray();
        double[] u=Enumerable.Range(0,n*n).Select(i=>4*Math.Sin(2*Math.PI*(i%n+1)/n)).ToArray();
        double[] v=Enumerable.Range(0,n*n).Select(i=>2*Math.Cos(2*Math.PI*(i/n+1)/n)).ToArray();
        var yes=new SheetYieldOptions(); var no=new SheetYieldOptions(enabled:false);
        ViscoplasticSheetSolution S(double[] a,double[] b,double[] m,double[] y,SheetYieldOptions o,ThinSheetSolution? warm=null)
            =>ViscoplasticSheet.Solve(a,b,m,y,n,1,1,1,o,warm);
        var linear=S(u,v,mu,load,no); var plastic=S(u,v,mu,load,yes);
        Check("new quadrature agrees with the legacy homogeneous linear sheet",()=>{
            var old=ThinSheetDeformation.Solve(u,v,mu,n,1,1,1);Arrays(old.East,linear.Velocity.East,1e-9);Arrays(old.South,linear.Velocity.South,1e-9);
        });
        Check("uniform translation produces no plastic excess",()=>{var a=S(Enumerable.Repeat(5d,n*n).ToArray(),Enumerable.Repeat(-3d,n*n).ToArray(),mu,load,yes);Require(a.PlasticRates.All(x=>x==0));Near(a.Velocity.Dissipation,0);});
        Check("actual yielding changes resolved velocities and satisfies force balance",()=>{Require(plastic.YieldedFraction.Any(x=>x>0));Require(plastic.PlasticRates.Any(x=>x>0));Require(plastic.Velocity.East.Zip(linear.Velocity.East).Max(p=>Math.Abs(p.First-p.Second))>.01);Require(plastic.Velocity.RelativeResidual<=1e-12);});
        Check("external work equals regularized dissipation",()=>Near(plastic.Velocity.Work,plastic.Velocity.Dissipation,1e-8));
        Check("accepted solve decreases convex potential",()=>Require(plastic.FinalPotential<=plastic.InitialPotential));
        Check("warm start does not change the resolved solution",()=>{var a=S(u,v,mu,load,yes,linear.Velocity);Arrays(a.Velocity.East,plastic.Velocity.East,1e-9);Arrays(a.Velocity.South,plastic.Velocity.South,1e-9);});
        Check("load reversal reverses velocity not plastic activity",()=>{var a=S(u.Select(x=>-x).ToArray(),v.Select(x=>-x).ToArray(),mu,load,yes);Arrays(a.Velocity.East,plastic.Velocity.East.Select(x=>-x).ToArray(),1e-9);Arrays(a.PlasticRates,plastic.PlasticRates,1e-9);});
        Check("high yield loads recover the ductile control",()=>{var a=S(u,v,mu,Enumerable.Repeat(1e9,n*n).ToArray(),yes);Arrays(a.Velocity.East,linear.Velocity.East,1e-9);Require(a.PlasticRates.All(x=>x==0));});
        Check("zero coupling has no stress and leaves preferred velocities",()=>{var a=ViscoplasticSheet.Solve(u,v,mu,load,n,1,1,0,yes);Arrays(a.Velocity.East,u);Require(a.PlasticRates.All(x=>x==0));});
        Check("uniform metric scaling preserves velocities and integrated strain",()=>{var a=ViscoplasticSheet.Solve(u,v,mu,load,n,3,3,3,yes);Arrays(a.Velocity.East,plastic.Velocity.East,1e-9);Arrays(a.PlasticRates.Select(x=>3*x).ToArray(),plastic.PlasticRates,1e-9);});
        Check("invalid sizes warm starts and numerical budgets are refused",()=>{Throws(()=>ViscoplasticSheet.Solve(u[..3],v,mu,load,n,1,1,1,yes));Throws(()=>ViscoplasticSheet.Solve(u,v,mu,load,n,0,1,1,yes));Throws(()=>ViscoplasticSheet.Solve(u,v,mu,load,n,1,1,1,yes,maximumNewtonIterations:0));});
        Check("insufficient nonlinear budget cannot be labelled success",()=>Throws(()=>ViscoplasticSheet.Solve(u,v,mu,load,n,1,1,1,yes,maximumNewtonIterations:1)));
        Check("plastic memory identity survives production advection and relaxation",()=>{
            double[] c=Enumerable.Repeat(35d,16).ToArray(),zero=new double[16];var memory=new ContinentalStrainMemory(4,c,kind:ContinentalStrainKind.PlasticExcess);
            var a=memory.Accumulate(Enumerable.Repeat(.1,16).ToArray(),2).Advect(zero,zero,1,1,1).Relax(c,1,1,1,0);
            Require(a.Kind==ContinentalStrainKind.PlasticExcess);Near(a.Strain(0),.2);Near(memory.Strain(0),0);
        });
        Check("unchanged legacy option serialization excludes the new null option",()=>Require(!System.Text.Json.JsonSerializer.Serialize(new StrainWeakeningOptions()).Contains("Yield")));
        Check("return constitutive and heterogeneous spatial data for independent oracle",()=>{
            double[] heterogeneous=Enumerable.Range(0,n*n).Select(i=>1+.25*(i%5)).ToArray();var a=S(u,v,heterogeneous,load,yes);
            fixtures.Add(new{side=n,dx=1d,dz=1d,coupling=1d,preferredEast=u,preferredSouth=v,viscosity=heterogeneous,loads=load,options=yes,solution=a});
        });
        Check("actual Core generates paired small whole worlds with typed plastic memory",()=>{
            var scale=new TectonicScalePlan(1000000,1000000);var settings=new TectonicEvolutionSettings(side:32,duration:2);
            var legacy=TectonicHistory.Generate(73,scale,new(side:32,duration:0));var assemblage=ContinentalAssemblage.Generate(73,scale,32,CrustTransport.Sum(legacy.Initial.ContinentalKm)/1024);
            var a=ViscoplasticWorld.Generate(73,scale,settings,assemblage,new(mechanicalSide:16,yieldOptions:new(enabled:false)));
            var b=ViscoplasticWorld.Generate(73,scale,settings,assemblage,new(mechanicalSide:16,yieldOptions:new()));
            Require(a.History.InitialMaterialChecksum==b.History.InitialMaterialChecksum);Require(a.Memory.Moment.All(x=>x==0));Require(b.Memory.Kind==ContinentalStrainKind.PlasticExcess);Require(b.Memory.ProducedMoment>0);
            Require(a.History.Final.ElevationKm.Zip(b.History.Final.ElevationKm).Max(p=>Math.Abs(p.First-p.Second))>1e-8);
        });
        return new{status="PASS",count=names.Count,checks=names,fixtures,scope="NUMERIC_NOT_GEOGRAPHIC",erosion="NOT_RUN"};
    }
    private static void Require(bool valid){if(!valid)throw new InvalidOperationException("Yield check failed.");}
    private static void Near(double a,double b,double tolerance=1e-10){if(!double.IsFinite(a)||Math.Abs(a-b)>tolerance)throw new InvalidOperationException($"Expected {b:R}, got {a:R}, tolerance {tolerance:R}.");}
    private static void Arrays(IReadOnlyList<double> a,IReadOnlyList<double> b,double tolerance=1e-10){Require(a.Count==b.Count);for(int i=0;i<a.Count;i++)Near(a[i],b[i],tolerance);}
    private static void Throws(Action action){try{action();}catch(ArgumentException){return;}catch(ArithmeticException){return;}throw new InvalidOperationException("Expected explicit refusal.");}
}
