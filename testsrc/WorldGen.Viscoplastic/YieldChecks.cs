using ISRWorldGen.Core.Geology.Evolution;

internal static class YieldChecks
{
    internal static object Run()
    {
        var checks=new List<string>();var oracles=new List<object>();
        void Case(string label,Action run){run();checks.Add(label);Console.Error.WriteLine("CHECK PASS: "+label);}
        void Assert(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        void Near(double a,double b,double tol=1e-9){Assert(Math.Abs(a-b)<=tol,$"{a:R} != {b:R}");}
        void Refuse(Action run){try{run();}catch(ArgumentException){return;}catch(ArithmeticException){return;}throw new InvalidOperationException("Invalid input accepted");}
        Case("invalid constitutive options rejected",()=>{Refuse(()=>new PlasticSheetOptions(double.NaN));Refuse(()=>new PlasticSheetOptions(postYieldRatio:0));Refuse(()=>ViscoplasticSheet.Flow(-1,1,1,.2));Refuse(()=>ViscoplasticSheet.Flow(1,-1,1,.2));});
        Case("zero rate stores no plastic history",()=>{var f=ViscoplasticSheet.Flow(3,0,.2,.2);Near(f.Viscosity,3,0);Near(f.PlasticRate,0,0);Near(f.Stress,0,0);});
        Case("subyield rate is wholly ductile",()=>{var f=ViscoplasticSheet.Flow(3,.02,.2,.2);Near(f.Viscosity,3,0);Near(f.PlasticRate,0,0);Near(f.ViscousRate,.02,0);});
        Case("exact threshold has no plastic rate",()=>Near(ViscoplasticSheet.Flow(3,.2/6,.2,.2).PlasticRate,0,0));
        Case("postyield strain splits into viscous and plastic parts",()=>{var f=ViscoplasticSheet.Flow(3,.1,.2,.2);Near(f.PlasticRate,.05333333333333334);Near(f.ViscousRate+f.PlasticRate,.1);Near(f.Stress,.28);});
        Case("series stress matches both constitutive branches",()=>{for(int k=1;k<=50;k++){double e=k*.01;var f=ViscoplasticSheet.Flow(3,e,.2,.2);Near(f.Stress,6*f.ViscousRate);if(f.PlasticRate>0)Near(f.Stress,.2+1.5*f.PlasticRate);}});
        Case("postyield tangent stays positive and potential convex",()=>{double prev=0;for(int k=0;k<100;k++){var f=ViscoplasticSheet.Flow(2,k*.01,.2,.2);Assert(f.Stress>=prev&&f.Potential>=0,"nonconvex");Assert(f.Viscosity+f.ViscosityDerivative*(k*.01)>=.4-1e-12,"lost ellipticity");prev=f.Stress;}});
        Case("potential derivative equals twice invariant stress",()=>{foreach(double e in new[]{.01,.08,.5}){double d=1e-6;double slope=(ViscoplasticSheet.Flow(3,e+d,.2,.2).Potential-ViscoplasticSheet.Flow(3,e-d,.2,.2).Potential)/(2*d);Near(slope,2*ViscoplasticSheet.Flow(3,e,.2,.2).Stress,1e-8);}});
        Case("unit residual tangent disables plasticity",()=>{var f=ViscoplasticSheet.Flow(2,100,.2,1);Near(f.Viscosity,2,0);Near(f.PlasticRate,0,0);});
        int n=8,count=n*n;double[] zero=new double[count],mu=Enumerable.Repeat(2d,count).ToArray(),ys=Enumerable.Repeat(.15,count).ToArray();
        var pe=Enumerable.Range(0,count).Select(i=>Math.Sin(2*Math.PI*(i%n+1)/n)+.3*Math.Cos(2*Math.PI*(i/n+.5)/n)).ToArray();
        var ps=Enumerable.Range(0,count).Select(i=>.4*Math.Cos(2*Math.PI*(i%n+.5)/n)-.7*Math.Sin(2*Math.PI*(i/n+1)/n)).ToArray();
        PlasticSheetSolution Solve(PlasticSheetOptions o)=>ViscoplasticSheet.Solve(pe,ps,mu,ys,n,1,1.3,1.2,o);
        Case("uniform translation is an exact zero dissipation solution",()=>{var s=ViscoplasticSheet.Solve(Enumerable.Repeat(2d,count).ToArray(),Enumerable.Repeat(-3d,count).ToArray(),mu,ys,n,1,1,2,new());foreach(double a in s.Native.East)Near(a,2,0);Near(s.Native.Dissipation,0,0);Assert(s.PlasticRates.All(x=>x==0),"uniform yielding");});
        Case("disabled plastic operator matches uniform viscosity linear symbol",()=>{double[] u=Enumerable.Range(0,count).Select(i=>Math.Sin(2*Math.PI*(i%n+1)/n)).ToArray();var s=ViscoplasticSheet.Solve(u,zero,mu,ys,n,1,1,2,new(enabled:false));double k=2*Math.Sin(Math.PI/n);for(int i=0;i<count;i++)Near(s.Native.East[i],u[i]/(1+32*k*k));Assert(s.PlasticRates.All(x=>x==0),"control plastic memory");});
        var active=Solve(new());var control=Solve(new(enabled:false));
        Case("actual nonlinear residual and energy descent",()=>{Assert(active.Native.RelativeResidual<=1e-10,"residual");Assert(active.FinalEnergy<=active.InitialEnergy,"energy increase");Assert(active.NewtonIterations>0,"no solve");});
        Case("yield changes force balanced velocities",()=>Assert(active.Native.East.Zip(control.Native.East,(a,b)=>Math.Abs(a-b)).Max()>1e-4,"inactive experiment"));
        Case("work equals physical dissipation",()=>Near(active.Native.Work,active.Native.Dissipation,1e-7));
        Case("mean motion remains the imposed mean",()=>Assert(active.Native.MeanVelocityError<1e-9,"mean drift"));
        Case("local plastic work is nonnegative",()=>{for(int i=0;i<count;i++){Assert(active.PlasticRates[i]>=0&&active.PlasticRates[i]<=active.Rates[i],"invalid rate split");Assert(active.Stress[i]*active.PlasticRates[i]>=0,"negative work");}});
        Case("warm start converges to the same unique solution",()=>{var b=ViscoplasticSheet.Solve(pe,ps,mu,ys,n,1,1.3,1.2,new(),control.Native);for(int i=0;i<count;i++){Near(active.Native.East[i],b.Native.East[i],2e-9);Near(active.Native.South[i],b.Native.South[i],2e-9);}});
        Case("coordinate exchange rotates the full mechanical solution",()=>{
            double[] Trans(IReadOnlyList<double> a)=>Enumerable.Range(0,count).Select(i=>a[(i%n)*n+i/n]).ToArray();
            var b=ViscoplasticSheet.Solve(Trans(ps),Trans(pe),Trans(mu),Trans(ys),n,1.3,1,1.2,new());
            for(int i=0;i<count;i++){int j=(i%n)*n+i/n;Near(active.Native.East[i],b.Native.South[j],2e-9);Near(active.PlasticRates[i],b.PlasticRates[j],2e-9);}});
        Case("rigid translation of the forcing adds no strain",()=>{var b=ViscoplasticSheet.Solve(pe.Select(a=>a+2).ToArray(),ps.Select(a=>a-3).ToArray(),mu,ys,n,1,1.3,1.2,new());for(int i=0;i<count;i++){Near(b.Native.East[i],active.Native.East[i]+2,2e-9);Near(b.PlasticRates[i],active.PlasticRates[i],2e-9);}});
        Case("invalid solve is refused rather than silently clipped",()=>{Refuse(()=>ViscoplasticSheet.Solve(pe,ps,mu,ys,n,0,1,1,new()));Refuse(()=>ViscoplasticSheet.Solve(pe,ps,mu,ys.Select(_=>0d).ToArray(),n,1,1,1,new()));});
        Case("export actual heterogeneous nonlinear fixture for independent oracle",()=>{
            var heterogeneous=Enumerable.Range(0,count).Select(i=>1+.02*i).ToArray();
            var s=ViscoplasticSheet.Solve(pe,ps,heterogeneous,ys,n,1,1.3,1.2,new());
            oracles.Add(new{side=n,dx=1d,dz=1.3,coupling=1.2,pe,ps,mu=heterogeneous,yieldStress=ys,ratio=.2,result=s});});
        Case("plastic memory source is strictly gated and transported",()=>{
            var c=Enumerable.Repeat(35d,count).ToArray();var m=new ContinentalStrainMemory(n,c);
            var z=m.Accumulate(control.PlasticRates,1);Near(z.ProducedMoment,0,0);
            var p=m.Accumulate(active.PlasticRates,1);Assert(p.ProducedMoment>0,"no plastic source");
            var adv=p.Advect(Enumerable.Repeat(.1,count).ToArray(),zero,1,1,1);Near(CrustTransport.Sum(adv.Moment),CrustTransport.Sum(p.Moment),1e-9);});
        Case("small complete histories retain initial material and origin budgets",()=>{
            var scale=new TectonicScalePlan(1_000_000,1_000_000);var old=TectonicHistory.Generate(73,scale,new(side:32,duration:0));
            var a=ContinentalAssemblage.Generate(73,scale,32,CrustTransport.Sum(old.Initial.ContinentalKm)/1024);
            var settings=new TectonicEvolutionSettings(side:32,duration:2);
            var p=MaterialBoundHistory.GenerateWithWeakening(73,scale,settings,a,new(mechanicalSide:16,plasticity:new()));
            var c=MaterialBoundHistory.GenerateWithWeakening(73,scale,settings,a,new(mechanicalSide:16,plasticity:new(enabled:false)));
            Assert(p.History.InitialMaterialChecksum==c.History.InitialMaterialChecksum,"different initialization");Near(c.Memory.ProducedMoment,0,0);
            for(int i=0;i<p.History.Plates.Count;i++)Near(p.History.InitialContinentalByOrigin[i],p.History.FinalContinentalByOrigin[i],1e-7);
            Assert(p.Policy!=c.Policy&&p.FinalPlasticity is not null,"missing constitutive identity");});
        return new{status="PASS_NUMERICAL_NOT_GEOGRAPHIC",checks,oracles,algorithm=ViscoplasticSheet.AlgorithmId};
    }
}
