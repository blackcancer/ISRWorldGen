using ISRWorldGen.Core.Geology.Evolution;

internal static class PowerLawChecks
{
    internal static string[] Run()
    {
        var names = new List<string>(); const int n = 16, count = n*n;
        double[] zero = new double[count], one = Enumerable.Repeat(1d,count).ToArray();
        double[] mu = Enumerable.Range(0,count).Select(i=>.4+(i%13)/5d).ToArray();
        double[] a = Enumerable.Range(0,count).Select(i=>Math.Sin(.31*i)).ToArray();
        double[] b = Enumerable.Range(0,count).Select(i=>Math.Cos(.17*i)).ToArray();
        var options = new PowerLawSheetOptions(3,.05);
        Case("power-law zero forcing creates no movement",()=>
        {
            var r=PowerLawSheetDeformation.Solve(zero,zero,mu,n,1,2,2,options);
            Require(r.Solution.East.All(v=>v==0)&&r.Solution.South.All(v=>v==0),"spontaneous velocity");
            Near(r.Solution.Dissipation,0,0);
        });
        Case("power-law common translation has zero strain regardless of material",()=>
        {
            var r=PowerLawSheetDeformation.Solve(one,one.Select(v=>-2*v).ToArray(),mu,n,1,2,2,options);
            for(int i=0;i<count;i++){Near(r.Solution.East[i],1,1e-12);Near(r.Solution.South[i],-2,1e-12);}
        });
        Case("stress exponent one recovers the exact original linear solution",()=>
        {
            var old=ThinSheetDeformation.Solve(a,b,mu,n,1,2,2);
            var r=PowerLawSheetDeformation.Solve(a,b,mu,n,1,2,2,new(1,.05));
            Require(old.East.SequenceEqual(r.Solution.East)&&old.South.SequenceEqual(r.Solution.South),"linear limit drift");
        });
        Case("zero coupling keeps prescribed face velocities",()=>
        {
            var r=PowerLawSheetDeformation.Solve(a,b,mu,n,1,2,0,options);
            Require(a.SequenceEqual(r.Solution.East)&&b.SequenceEqual(r.Solution.South),"zero length changes forcing");
        });
        var op=new PowerLawSheetOperator(a,b,mu,n,1,2,2,options);
        double[] trial=a.Select(v=>.3*v).Concat(b.Select(v=>.4*v)).ToArray();
        double[] direction=Enumerable.Range(0,2*count).Select(i=>Math.Sin(.49*i)).ToArray();
        var measured=op.Evaluate(trial);double[] tangent=new double[2*count];op.Tangent(direction,tangent);
        const double h=1e-5;
        var plus=op.Evaluate(trial.Select((v,i)=>v+h*direction[i]).ToArray());
        var minus=op.Evaluate(trial.Select((v,i)=>v-h*direction[i]).ToArray());
        Case("nonlinear stress is the derivative of its two-dimensional discrete potential",()=>
            Near((plus.Energy-minus.Energy)/(2*h),PowerLawSheetOperator.Dot(measured.Gradient,direction),1e-6));
        Case("exact Newton tangent agrees with finite differences of full forces",()=>
        {
            double err=0,scale=0;
            for(int i=0;i<tangent.Length;i++){err+=Math.Pow((plus.Gradient[i]-minus.Gradient[i])/(2*h)-tangent[i],2);scale+=tangent[i]*tangent[i];}
            Require(Math.Sqrt(err/scale)<1e-7,"tangent misses constitutive derivative");
        });
        Case("nonlinear tangent is symmetric and positive despite shear thinning",()=>
        {
            op.Evaluate(trial);double[] second=Enumerable.Range(0,2*count).Select(i=>Math.Cos(.23*i)).ToArray(),hs=new double[2*count];
            op.Tangent(second,hs);op.Tangent(direction,tangent);
            Near(PowerLawSheetOperator.Dot(second,tangent),PowerLawSheetOperator.Dot(direction,hs),1e-9);
            Require(PowerLawSheetOperator.Dot(direction,tangent)>PowerLawSheetOperator.Dot(direction,direction),"nonpositive membrane tangent");
        });
        Case("linear potential quadrature recovers heterogeneous harmonic-corner stresses",()=>
        {
            var linear=ThinSheetDeformation.Solve(a,b,mu,n,1,2,2);
            var linearOp=new PowerLawSheetOperator(a,b,mu,n,1,2,2,new(1,.05));
            var eval=linearOp.Evaluate(linear.East.Concat(linear.South).ToArray());
            Require(Math.Sqrt(PowerLawSheetOperator.Dot(eval.Gradient,eval.Gradient))<1e-10,"changed base stencil");
            Near(eval.Dissipation,linear.Dissipation,Math.Max(1,linear.Dissipation)*1e-12);
        });
        var solved=PowerLawSheetDeformation.Solve(a,b,mu,n,1,2,2,options);
        Case("accepted residual uses updated nonlinear stress rather than a frozen solve",()=>
        {
            var full=op.Evaluate(solved.Solution.East.Concat(solved.Solution.South).ToArray());
            double r=Math.Sqrt(PowerLawSheetOperator.Dot(full.Gradient,full.Gradient))/Math.Max(1,Math.Sqrt(PowerLawSheetOperator.Dot(op.Forcing,op.Forcing)));
            Require(r<=1e-12&&solved.Solution.RelativeResidual<=1e-12,"unconverged full constitutive balance");
        });
        Case("nonlinear basal work equals positive viscous dissipation",()=>
        {Require(solved.Solution.Dissipation>0,"negative dissipation");Near(solved.Solution.Work,solved.Solution.Dissipation,1e-8);});
        Case("nonlinear iteration decreases the declared potential",()=>
            Require(solved.FinalEnergy<solved.InitialEnergy&&solved.NewtonIterations>0,"not an energy solve"));
        Case("strain dependence reduces effective viscosity without arbitrary clipping",()=>
        {for(int i=0;i<count;i++)Require(solved.EffectiveViscosity[i]>0&&solved.EffectiveViscosity[i]<=mu[i],"invalid effective viscosity");});
        Case("nonlinear internal stresses inject no net translation",()=>Require(solved.Solution.MeanVelocityError<1e-10,"net internal force"));
        Case("cold and warm nonlinear starts agree",()=>
        {
            var r=PowerLawSheetDeformation.Solve(a,b,mu,n,1,2,2,options,solved.Solution);
            for(int i=0;i<count;i++){Near(r.Solution.East[i],solved.Solution.East[i],1e-10);Near(r.Solution.South[i],solved.Solution.South[i],1e-10);}
        });
        Case("nonlinear law is invariant under reversal of forcing",()=>
        {
            var r=PowerLawSheetDeformation.Solve(a.Select(v=>-v).ToArray(),b.Select(v=>-v).ToArray(),mu,n,1,2,2,options);
            for(int i=0;i<count;i++){Near(r.Solution.East[i],-solved.Solution.East[i],1e-10);Near(r.Solution.South[i],-solved.Solution.South[i],1e-10);}
        });
        Case("axis exchange preserves the mixed normal and corner shear law",()=>
        {
            double[] T(IReadOnlyList<double> q)=>Enumerable.Range(0,count).Select(i=>q[(i%n)*n+i/n]).ToArray();
            var r=PowerLawSheetDeformation.Solve(T(b),T(a),T(mu),n,2,1,2,options);
            var e=T(solved.Solution.South);var s=T(solved.Solution.East);
            for(int i=0;i<count;i++){Near(r.Solution.East[i],e[i],1e-9);Near(r.Solution.South[i],s[i],1e-9);}
        });
        Case("coordinate rescaling also rescales the reference strain rate",()=>
        {
            var r=PowerLawSheetDeformation.Solve(a,b,mu,n,1000,2000,2000,new(3,.05/1000));
            for(int i=0;i<count;i++){Near(r.Solution.East[i],solved.Solution.East[i],1e-10);Near(r.Solution.South[i],solved.Solution.South[i],1e-10);}
        });
        Case("changing time units preserves the nonlinear solution",()=>
        {
            var r=PowerLawSheetDeformation.Solve(a.Select(v=>v/10).ToArray(),b.Select(v=>v/10).ToArray(),mu,n,1,2,2,new(3,.005));
            for(int i=0;i<count;i++)Near(r.Solution.East[i]*10,solved.Solution.East[i],1e-10);
        });
        Case("manufactured longitudinal power-law solution uses an independent one-dimensional stencil",()=>
        {
            double[] u=Enumerable.Range(0,count).Select(i=>Math.Sin(2*Math.PI*(i%n)/n)).ToArray(),stress=new double[count],forcing=new double[count];
            for(int i=0;i<count;i++)
            {int w=(i/n)*n+(i%n+n-1)%n;double ex=(u[i]-u[w])*2;double f=Math.Pow(1+ex*ex/Math.Pow(2*.05,2),-1d/3);stress[i]=4*f*ex;}
            for(int i=0;i<count;i++)forcing[i]=u[i]+2*(stress[i]-stress[(i/n)*n+(i%n+1)%n]);
            var r=PowerLawSheetDeformation.Solve(forcing,zero,one,n,1,1,2,options);
            for(int i=0;i<count;i++){Near(r.Solution.East[i],u[i],1e-9);Near(r.Solution.South[i],0,1e-9);}
        });
        Case("power-law pure tangential shear does not create artificial normal divergence",()=>
        {
            var shear=Enumerable.Range(0,count).Select(i=>Math.Cos(2*Math.PI*(i/n+.5)/n)).ToArray();
            var r=PowerLawSheetDeformation.Solve(shear,zero,one,n,1,1,2,options);
            Require(r.Solution.Divergence.Max(v=>Math.Abs(v))<1e-10,"shear generated a source/sink");
        });
        Case("nonlinear resistance concentrates a controlled shear contrast more than the linear law",()=>
        {
            var f=Enumerable.Range(0,count).Select(i=>Math.Tanh(8*Math.Cos(2*Math.PI*(i/n+.5)/n))).ToArray();
            var linear=ThinSheetDeformation.Solve(f,zero,one,n,1,1,2);
            var r=PowerLawSheetDeformation.Solve(f,zero,one,n,1,1,2,options);
            Require(r.Solution.EngineeringShear.Max(v=>Math.Abs(v))>linear.EngineeringShear.Max(v=>Math.Abs(v))*1.05,"no localized response");
        });
        Case("iteration exhaustion rejects a stale solution",()=>
            Refuse(()=>PowerLawSheetDeformation.Solve(a,b,mu,n,1,2,2,new(3,.05,maximumNewtonIterations:1))));
        Case("invalid power-law parameters and nonfinite input are refused",()=>
        {
            Refuse(()=>new PowerLawSheetOptions(0));Refuse(()=>new PowerLawSheetOptions(3,0));Refuse(()=>new PowerLawSheetOptions(double.NaN));
            Refuse(()=>PowerLawSheetDeformation.Solve(a,b,zero,n,1,2,2,options));
            Refuse(()=>PowerLawSheetDeformation.Solve(a,b,mu,n,0,2,2,options));
        });
        Case("mechanical comparisons keep all input material arrays unchanged",()=>
        {for(int i=0;i<count;i++){Near(a[i],Math.Sin(.31*i),0);Near(mu[i],.4+(i%13)/5d,0);}});
        var scale=new TectonicScalePlan(1_000_000,1_000_000);var settings=new TectonicEvolutionSettings(side:32,duration:2);
        var assemblage=ContinentalAssemblage.Generate(73,scale,32);MaterialBoundHistory? hist=null;
        Case("nonlinear mechanics drives conservative transport with the same initial material",()=>
        {
            hist=MaterialBoundHistory.GenerateWithRheology(73,scale,settings,assemblage,new(16,powerLaw:new()));
            Require(hist.Initial.ContinentalKm.SequenceEqual(assemblage.ContinentalKm),"new initial continent");
            for(int p=0;p<hist.Plates.Count;p++)Near(hist.InitialContinentalByOrigin[p],hist.FinalContinentalByOrigin[p],1e-8);
            Require(hist.MechanicalSolves.All(s=>s.RelativeResidual<=1e-12),"invalid nonlinear integration");
        });
        Case("homogeneous and nonlinear controls have distinct identity but identical initial fields",()=>
        {
            var control=MaterialBoundHistory.GenerateWithRheology(73,scale,settings,assemblage,new(16,homogeneousControl:true));
            Require(control.InitialMaterialChecksum==hist!.InitialMaterialChecksum&&control.Checksum!=hist.Checksum,"uncontrolled initial change");
        });
        Case("nonlinear atlas resizing is a scale change not a continent crop",()=>
        {foreach(long size in new long[]{131072,262144,1_000_000})Near(hist!.SampleElevationKm(new(size,size),.234*size,.619*size),hist!.SampleElevationKm(scale,234000,619000),1e-10);});
        return names.ToArray();
        void Case(string name,Action action){action();names.Add(name);Console.WriteLine("CHECK PASS: "+name);}
    }
    private static void Near(double a,double b,double tol){if(!double.IsFinite(a)||!double.IsFinite(b)||Math.Abs(a-b)>tol)throw new InvalidOperationException($"Power-law mismatch {a:R} != {b:R} tolerance {tol:R}");}
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Refuse(Action action){try{action();}catch(ArgumentException){return;}catch(ArithmeticException){return;}throw new InvalidOperationException("Invalid nonlinear state accepted");}
}
