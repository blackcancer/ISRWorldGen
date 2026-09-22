using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

internal static class WeakeningChecks
{
    internal static object Run()
    {
        var checks=new List<string>();var oracles=new List<object>();
        void Case(string label,Action body){body();checks.Add(label);Console.Error.WriteLine("CHECK PASS: "+label);}
        void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        void Near(double a,double b,double tol=1e-10){Require(Math.Abs(a-b)<=tol,$"{a:R} != {b:R}");}
        void Refuse(Action body){try{body();}catch(ArgumentException){return;}catch(ArithmeticException){return;}throw new InvalidOperationException("Invalid input accepted");}
        int n=16,count=n*n;double[] ones=Enumerable.Repeat(35d,count).ToArray(),zero=new double[count];
        var options=new StrainWeakeningOptions(n,supportLengthReference:0);
        var memory=new ContinentalStrainMemory(n,ones);
        Case("weakening options reject nonfinite or nonpositive constitutive data",()=>{
            Refuse(()=>new StrainWeakeningOptions(residualRatio:0));Refuse(()=>new StrainWeakeningOptions(strainScale:double.NaN));
            Refuse(()=>new StrainWeakeningOptions(mechanicalSide:15));Refuse(()=>options.Multiplier(-1));});
        Case("zero strain has full strength and increasing history approaches declared residual",()=>{
            Near(options.Multiplier(0),1);double previous=1;for(int i=0;i<100;i++){double f=options.Multiplier(i*.1);Require(f<=previous&&f>=.35,"invalid law");previous=f;}
            Near(options.Multiplier(100),.35);});
        Case("disabled feedback is identically one for every admissible memory",()=>{var c=new StrainWeakeningOptions(residualRatio:1);foreach(double k in new[]{0d,.2,10d,1e100})Near(c.Multiplier(k),1,0);});
        Case("oceanic viscosity is independent of continental weakening",()=>{Near(StrainWeakeningRheology.Viscosity(0,7,80,.35),StrainWeakeningRheology.Viscosity(0,7,80,1),0);});
        Case("continental viscosity responds without a plate weight parameter",()=>{double a=StrainWeakeningRheology.Viscosity(35,0,0,1);Near(a,3);Near(StrainWeakeningRheology.Viscosity(35,0,0,.35),a*.35);});
        Case("rigid rotation contains no symmetric viscous strain rate",()=>Near(ContinentalStrainMemory.Rate(0,0,-.3+.3),0,0));
        Case("simple shear accumulates strain without being classified as an opening",()=>Near(ContinentalStrainMemory.Rate(0,0,.4),.2));
        Case("invariant includes incompressible vertical compensation",()=>{Near(ContinentalStrainMemory.Rate(.2,0,0),.2);Near(ContinentalStrainMemory.Rate(.2,.2,0),Math.Sqrt(.12));});
        Case("rotating the symmetric strain tensor preserves its invariant",()=>{
            double a=.13,d=-.07,b=.09,theta=.73,c=Math.Cos(theta),s=Math.Sin(theta);
            double aa=c*c*a-2*c*s*b+s*s*d,dd=s*s*a+2*c*s*b+c*c*d,bb=c*s*(a-d)+(c*c-s*s)*b;
            Near(ContinentalStrainMemory.Rate(a,d,2*b),ContinentalStrainMemory.Rate(aa,dd,2*bb));});
        Case("invalid rates and overflow are refused",()=>{Refuse(()=>ContinentalStrainMemory.Rate(double.NaN,0,0));Refuse(()=>ContinentalStrainMemory.Rate(1e308,0,0));});
        Case("initial material and history are copied instead of retained mutable",()=>{var c=(double[])ones.Clone();var q=Enumerable.Repeat(.3,count).ToArray();var m=new ContinentalStrainMemory(n,c,q);c[0]=0;q[0]=0;Near(m.Carrier[0],35);Near(m.Strain(0),.3);});
        Case("explicit production closes its material-weighted moment budget",()=>{var m=memory.Accumulate(Enumerable.Repeat(.1,count).ToArray(),2);Near(m.Strain(0),.2);Near(m.ProducedMoment,35*count*.2);});
        Case("viscous memory is irreversible without an explicit healing law",()=>{var a=memory.Accumulate(Enumerable.Repeat(.1,count).ToArray(),1);var b=a.Accumulate(Enumerable.Repeat(.1,count).ToArray(),1);Near(b.Strain(0),.2);});
        Case("empty continental cells cannot acquire orphan memory",()=>{var m=new ContinentalStrainMemory(n,zero).Accumulate(ones,1);Require(m.Moment.All(v=>v==0),"orphan strain");});
        Case("uniform translation advects local history with its carrier",()=>{
            var initial=new double[count];initial[0]=1;var m=new ContinentalStrainMemory(n,ones,initial);
            var moved=m.Advect(Enumerable.Repeat(1d,count).ToArray(),zero,1,1,1);
            Near(moved.Strain(1),1);Near(moved.Strain(0),0);Near(CrustTransport.Sum(moved.Moment),35);});
        Case("advection through a periodic edge retains material history",()=>{
            var initial=new double[count];initial[n-1]=1;var m=new ContinentalStrainMemory(n,ones,initial);
            Near(m.Advect(Enumerable.Repeat(1d,count).ToArray(),zero,1,1,1).Strain(0),1);});
        Case("subnormal packet transport retains carrier to moment relation",()=>{
            var c=new double[4];c[0]=double.Epsilon;var m=new ContinentalStrainMemory(2,c,new[]{50d,0,0,0});
            var moved=m.Advect(Enumerable.Repeat(.125,4).ToArray(),new double[4],1,1,1);
            Require(moved.Carrier[1]==0&&moved.Moment[1]==0,"orphan subnormal memory");});
        Case("invalid CFL leaves the old immutable memory unchanged",()=>{
            string before=JsonSerializer.Serialize(memory);Refuse(()=>memory.Advect(ones,zero,1,1,1));Require(before==JsonSerializer.Serialize(memory),"mutated input");});
        Case("uniform concentration survives nonuniform continental transport",()=>{
            double[] c=Enumerable.Range(0,count).Select(i=>1d+i%17).ToArray();var m=new ContinentalStrainMemory(n,c,Enumerable.Repeat(.7,count).ToArray());
            var u=Enumerable.Range(0,count).Select(i=>.1*Math.Sin(i*.37)).ToArray();
            for(int k=0;k<30;k++)m=m.Advect(u,zero,1,1,.5);
            foreach(double q in Enumerable.Range(0,count).Select(m.Strain))Near(q,.7);});
        Case("lower crust transports memory together with its donor volume",()=>{
            var c=(double[])ones.Clone();c[0]=80;var q=new double[count];q[0]=1;
            var m=new ContinentalStrainMemory(n,c,q);var moved=m.Relax(c,1,1,.1,1);
            moved.RequireCarrier(CrustTransport.RelaxThickCrust(c,n,1,1,.1,1));Require(moved.Moment[1]>0&&moved.Moment[0]<80,"memory left behind");Near(CrustTransport.Sum(moved.Moment),80);});
        Case("subthreshold lower crust does not diffuse history independently",()=>{
            var m=new ContinentalStrainMemory(n,ones,Enumerable.Range(0,count).Select(i=>.001*i).ToArray());var moved=m.Relax(ones,1,1,.1,1);
            for(int i=0;i<count;i++)Near(m.Moment[i],moved.Moment[i],0);});
        Case("uniform body motion solves with zero viscous dissipation",()=>{
            var s=ThinSheetDeformation.Solve(Enumerable.Repeat(2d,count).ToArray(),Enumerable.Repeat(-1d,count).ToArray(),Enumerable.Repeat(3d,count).ToArray(),n,1,1,2);
            foreach(double u in s.East)Near(u,2);Near(s.Dissipation,0);});
        Case("longitudinal Fourier mode matches exact discrete sheet symbol",()=>{
            var forcing=Enumerable.Range(0,count).Select(i=>Math.Sin(2*Math.PI*(i%n+1)/n)).ToArray();double mu=2,l=2,k=2*Math.Sin(Math.PI/n);
            var s=ThinSheetDeformation.Solve(forcing,zero,Enumerable.Repeat(mu,count).ToArray(),n,1,1,l);
            for(int i=0;i<count;i++)Near(s.East[i],forcing[i]/(1+4*mu*l*l*k*k));});
        Case("transverse Fourier mode matches exact discrete sheet symbol",()=>{
            var forcing=Enumerable.Range(0,count).Select(i=>Math.Sin(2*Math.PI*(i%n+.5)/n)).ToArray();double mu=2,l=2,k=2*Math.Sin(Math.PI/n);
            var s=ThinSheetDeformation.Solve(zero,forcing,Enumerable.Repeat(mu,count).ToArray(),n,1,1,l);
            for(int i=0;i<count;i++)Near(s.South[i],forcing[i]/(1+mu*l*l*k*k));});
        int[] owners=Enumerable.Range(0,count).Select(i=>i%n<n/2?0:1).ToArray();
        var state=MaterialPlateCohorts.Create(n,2,owners,ones,zero,zero,zero);
        TectonicPlate[] plates=[new(0,4000,8000,1000,0),new(1,12000,8000,-1000,0)];
        var initialMemory=new ContinentalStrainMemory(n,ones);
        Case("zero-memory coupled frames equal the feedback-disabled control",()=>{
            var a=StrainWeakeningRheology.Solve(state,plates,initialMemory,1000,1000,2200,new(n,supportLengthReference:2000));
            var b=StrainWeakeningRheology.Solve(state,plates,initialMemory,1000,1000,2200,new(n,residualRatio:1,supportLengthReference:2000));
            for(int i=0;i<count;i++)Near(a.East[i],b.East[i],0);});
        Case("transported weakness feeds back into the actual two-dimensional equilibrium",()=>{
            var values=Enumerable.Range(0,count).Select(i=>i%n>=6&&i%n<=9?1d:0d).ToArray();var m=new ContinentalStrainMemory(n,ones,values);
            var a=StrainWeakeningRheology.Solve(state,plates,m,1000,1000,2200,new(n,supportLengthReference:2000));
            var b=StrainWeakeningRheology.Solve(state,plates,m,1000,1000,2200,new(n,residualRatio:1,supportLengthReference:2000));
            Require(a.East.Zip(b.East,(x,y)=>Math.Abs(x-y)).Max()>.01,"rheology did not change solved velocity");
            Require(a.Native.RelativeResidual<=1e-12,"unresolved balance");Near(a.Native.Work,a.Native.Dissipation,1e-8*Math.Max(1,a.Native.Dissipation));
        });
        Case("metric rescaling preserves strain and relative rheology",()=>{
            var m=new ContinentalStrainMemory(n,ones,Enumerable.Repeat(.3,count).ToArray());
            var a=StrainWeakeningRheology.Solve(state,plates,m,1000,1000,2200,new(n,supportLengthReference:2000));
            var scaled=plates.Select(p=>p with{Vx=p.Vx*2,Vz=p.Vz*2}).ToArray();
            var b=StrainWeakeningRheology.Solve(state,scaled,m,2000,2000,4400,new(n,supportLengthReference:4000));
            for(int i=0;i<count;i++){Near(a.East[i]*2,b.East[i],1e-7);Near(a.Rates[i],b.Rates[i],1e-10);}});
        Case("nonuniform resolved equilibrium exports for independent matrix oracle",()=>{
            const int side=8;var u=Enumerable.Range(0,64).Select(i=>Math.Sin(i*.37)).ToArray();var v=Enumerable.Range(0,64).Select(i=>Math.Cos(i*.23)).ToArray();
            var mu=Enumerable.Range(0,64).Select(i=>.4+(i%7)*.2).ToArray();var s=ThinSheetDeformation.Solve(u,v,mu,side,2,3,5);
            oracles.Add(new{side,dx=2d,dz=3d,coupling=5d,preferredEast=u,preferredSouth=v,viscosity=mu,solution=s});});
        Case("new coupling runs from the same whole-atlas material state and changes heights",()=>{
            var scale=new TectonicScalePlan(1_000_000,1_000_000);var settings=new TectonicEvolutionSettings(side:64,duration:4);
            var old=MaterialBoundHistory.Generate(73,scale,new(side:64,duration:0));
            var assemblage=ContinentalAssemblage.Generate(73,scale,64,CrustTransport.Sum(old.Initial.ContinentalKm)/4096);
            var a=MaterialBoundHistory.GenerateWithWeakening(73,scale,settings,assemblage,new(16,residualRatio:1));
            var b=MaterialBoundHistory.GenerateWithWeakening(73,scale,settings,assemblage,new(16));
            Require(a.History.InitialMaterialChecksum==b.History.InitialMaterialChecksum,"different initial conditions");
            Require(a.History.EvolutionPolicy!=b.History.EvolutionPolicy,"missing algorithm identity");
            Require(b.Memory.ProducedMoment>0,"missing strain production");
            Require(a.History.Final.ElevationKm.Zip(b.History.Final.ElevationKm,(x,y)=>Math.Abs(x-y)).Max()>1e-9,"passive instead of coupled");
            foreach(long size in new[]{131072L,262144L,1000000L})Near(b.History.SampleElevationKm(new(size,size),.375*size,.625*size),b.History.SampleElevationKm(scale,375000,625000),1e-10);
        });
        return new {status="PASS_NUMERICAL_NOT_GEOGRAPHIC",count=checks.Count,checks,oracles};
    }
}
