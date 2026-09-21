using ISRWorldGen.Core.Geology.Evolution;

internal static class MembraneChecks
{
    internal static string[] Run()
    {
        var cases = new List<string>(); const int n = 16, count = n * n;
        double[] ones = Enumerable.Repeat(1d, count).ToArray(), zero = new double[count];
        Case("zero forcing gives exactly zero motion and dissipation", () =>
        {
            var s = LithosphereMembrane.Solve(n, 1, 1, 2, ones, zero, zero);
            Require(s.East.All(v => v == 0) && s.South.All(v => v == 0) && s.ViscousDissipation == 0, "spontaneous motion");
        });
        Case("heterogeneous resistance preserves common plate translation", () =>
        {
            double[] eta = Enumerable.Range(0, count).Select(i => .2 + i % 13).ToArray();
            var s = LithosphereMembrane.Solve(n, 1, 1, 2, eta, Enumerable.Repeat(2d, count).ToArray(), Enumerable.Repeat(-3d, count).ToArray());
            foreach (double v in s.East) Near(v, 2, 1e-9);
            foreach (double v in s.South) Near(v, -3, 1e-9);
        });
        Case("zero coupling is the exact local drag equation", () =>
        {
            double[] u = Enumerable.Range(0, count).Select(i => Math.Sin(i)).ToArray();
            var s = LithosphereMembrane.Solve(n, 1, 1, 0, ones, u, zero);
            for (int i = 0; i < count; i++) Near(s.East[i], u[i], 1e-14);
        });
        Case("longitudinal Fourier response has the thin-sheet factor four", () =>
        {
            double[] wave = Enumerable.Range(0, count).Select(i => Math.Sin(Math.Tau * (i % n) / n)).ToArray();
            var s = LithosphereMembrane.Solve(n, 1, 1, 2, ones, wave, zero);
            double eigen = 1 + 16 * 4 * Math.Pow(Math.Sin(Math.PI / n), 2);
            for (int i = 0; i < count; i++) { Near(s.East[i], wave[i] / eigen, 1e-10); Near(s.South[i], 0, 1e-10); }
        });
        Case("transverse Fourier response retains shear rather than scalar compression", () =>
        {
            double[] wave = Enumerable.Range(0, count).Select(i => Math.Sin(Math.Tau * (i % n) / n)).ToArray();
            var s = LithosphereMembrane.Solve(n, 1, 1, 2, ones, zero, wave);
            double eigen = 1 + 4 * 4 * Math.Pow(Math.Sin(Math.PI / n), 2);
            for (int i = 0; i < count; i++) { Near(s.South[i], wave[i] / eigen, 1e-10); Near(s.East[i], 0, 1e-10); }
        });
        Case("rectangular metric is invariant to consistent length-unit conversion", () =>
        {
            double[] u = Enumerable.Range(0, count).Select(i => Math.Sin(i * .13)).ToArray();
            var a = LithosphereMembrane.Solve(n, 2, 3, 2.5, ones, u, zero);
            var b = LithosphereMembrane.Solve(n, 2000, 3000, 2500, ones, u, zero);
            for (int i = 0; i < count; i++) { Near(a.East[i], b.East[i], 1e-9); Near(a.South[i], b.South[i], 1e-9); }
        });
        Case("reversing the forcing reverses the solved velocities", () =>
        {
            double[] u = Enumerable.Range(0, count).Select(i => Math.Cos(i * .2)).ToArray();
            var a = LithosphereMembrane.Solve(n, 1, 1, 1, ones, u, zero);
            var b = LithosphereMembrane.Solve(n, 1, 1, 1, ones, u.Select(v => -v).ToArray(), zero);
            for (int i = 0; i < count; i++) { Near(a.East[i], -b.East[i], 1e-12); Near(a.South[i], -b.South[i], 1e-12); }
        });
        Case("axes transpose covariantly with the material field and forcing", () =>
        {
            double[] eta = Enumerable.Range(0, count).Select(i => .5 + (i % 7) * .3).ToArray();
            double[] u = Enumerable.Range(0, count).Select(i => Math.Sin(i * .2)).ToArray();
            var a = LithosphereMembrane.Solve(n, 2, 3, 2, eta, u, zero);
            var b = LithosphereMembrane.Solve(n, 3, 2, 2, Transpose(eta), zero, Transpose(u));
            for (int i = 0; i < count; i++) { int t = i % n * n + i / n; Near(a.East[i], b.South[t], 1e-9); Near(a.South[i], b.East[t], 1e-9); }
        });
        Case("independent dense variational matrix reproduces the MAC solve", () =>
        {
            const int m = 4, c = 16; double dx = 2, dz = 3, length = 1.5;
            var eta = Enumerable.Range(0, c).Select(i => .4 + (i % 5) * .3).ToArray();
            var u = Enumerable.Range(0, c).Select(i => Math.Cos(i * .43)).ToArray();
            var v = Enumerable.Range(0, c).Select(i => Math.Sin(i * .39)).ToArray();
            var actual = LithosphereMembrane.Solve(m, dx, dz, length, eta, u, v);
            double[,] matrix = new double[2*c, 2*c]; for(int i=0;i<2*c;i++) matrix[i,i]=1;
            for(int row=0;row<m;row++) for(int col=0;col<m;col++)
            {
                int i=row*m+col,w=row*m+(col+m-1)%m,nn=((row+m-1)%m)*m+col,e=row*m+(col+1)%m,s=((row+1)%m)*m+col,se=((row+1)%m)*m+(col+1)%m;
                var ex=new double[2*c];ex[i]=1/dx;ex[w]=-1/dx;
                var ez=new double[2*c];ez[c+i]=1/dz;ez[c+nn]=-1/dz;
                var gamma=new double[2*c];gamma[s]=1/dz;gamma[i]=-1/dz;gamma[c+e]=1/dx;gamma[c+i]=-1/dx;
                double mu=eta[i]*length*length, corner=.25*(eta[i]+eta[e]+eta[s]+eta[se])*length*length;
                for(int a=0;a<2*c;a++) for(int b=0;b<2*c;b++)
                    matrix[a,b]+=4*mu*(ex[a]*ex[b]+ez[a]*ez[b])+2*mu*(ex[a]*ez[b]+ez[a]*ex[b])+corner*gamma[a]*gamma[b];
            }
            var expected=Gaussian(matrix,u.Concat(v).ToArray());
            for(int i=0;i<c;i++){Near(actual.East[i],expected[i],1e-10);Near(actual.South[i],expected[c+i],1e-10);}
        });
        Case("driving work equals positive drag plus viscous dissipation", () =>
        {
            double[] u=Enumerable.Range(0,count).Select(i=>Math.Sin(i*.19)+.2).ToArray();
            var s=LithosphereMembrane.Solve(n,1,1,2,ones,u,zero);
            Near(s.DrivingWork,s.DragDissipation+s.ViscousDissipation,1e-8);
            Require(s.ViscousDissipation>0 && s.DragDissipation>0 && s.RelativeForceResidual<1e-9,"invalid energy or residual");
        });
        Case("stronger uniform lithosphere strains less under the same basal forcing", () =>
        {
            double[] u=Enumerable.Range(0,count).Select(i=>Math.Sin(Math.Tau*(i%n)/n)).ToArray();
            var weak=LithosphereMembrane.Solve(n,1,1,2,ones,u,zero);
            var strong=LithosphereMembrane.Solve(n,1,1,2,ones.Select(v=>v*8).ToArray(),u,zero);
            Require(strong.Divergence.Max(Math.Abs)<weak.Divergence.Max(Math.Abs)/2,"resistance ignored");
        });
        Case("a weak shear belt concentrates deformation without changing the forcing", () =>
        {
            double[] eta=Enumerable.Range(0,count).Select(i=>i%n is >=6 and <=9?.1:10d).ToArray();
            double[] v=Enumerable.Range(0,count).Select(i=>Math.Sin(Math.Tau*(i%n-7.5)/n)).ToArray();
            var heterogeneous=LithosphereMembrane.Solve(n,1,1,3,eta,zero,v);
            var homogeneous=LithosphereMembrane.Solve(n,1,1,3,Enumerable.Repeat(eta.Average(),count).ToArray(),zero,v);
            double shearA=Enumerable.Range(0,count).Where(i=>i%n is >=6 and <=8).Average(i=>Math.Abs(heterogeneous.Shear[i]));
            double shearB=Enumerable.Range(0,count).Where(i=>i%n is >=6 and <=8).Average(i=>Math.Abs(homogeneous.Shear[i]));
            Require(shearA>1.2*shearB,"weak belt not expressed in deformation");
            Console.WriteLine($"WEAK_BELT_SHEAR_RATIO={shearA/shearB:R}");
        });
        Case("declared compression width has the exact reference-column Fourier response", () =>
        {
            double width = 2.5;
            double coefficient = MembraneCoupling.CoefficientLengthForCompressionWidth(width);
            double reference = LithosphereMembrane.MaterialResistance(35, 0, 0);
            Near(4 * coefficient * coefficient * reference, width * width, 1e-13);
            double[] wave = Enumerable.Range(0, count).Select(i => Math.Sin(Math.Tau * (i % n) / n)).ToArray();
            var solution = LithosphereMembrane.Solve(n, 1, 1, coefficient,
                Enumerable.Repeat(reference, count).ToArray(), wave, zero);
            double eigen = 1 + 4 * width * width * Math.Pow(Math.Sin(Math.PI / n), 2);
            for (int i = 0; i < count; i++) Near(solution.East[i], wave[i] / eigen, 1e-10);
        });
        Case("compression-scale conversion is metric, finite and does not change materials", () =>
        {
            Near(MembraneCoupling.CoefficientLengthForCompressionWidth(0), 0, 0);
            Near(MembraneCoupling.CoefficientLengthForCompressionWidth(22000) * 1000,
                MembraneCoupling.CoefficientLengthForCompressionWidth(22000000), 1e-8);
            Refuse(() => MembraneCoupling.CoefficientLengthForCompressionWidth(-1));
            Refuse(() => MembraneCoupling.CoefficientLengthForCompressionWidth(double.NaN));
            Refuse(() => MembraneCoupling.CoefficientLengthForCompressionWidth(double.PositiveInfinity));
        });
        Case("material resistance follows thickness and ocean age, not a random plate mass", () =>
        {
            Require(LithosphereMembrane.MaterialResistance(35,0,0)>LithosphereMembrane.MaterialResistance(20,0,0),"continental thickness unused");
            Require(LithosphereMembrane.MaterialResistance(0,7,100)>LithosphereMembrane.MaterialResistance(0,7,0),"ocean age unused");
            Near(LithosphereMembrane.MaterialResistance(35,0,200),LithosphereMembrane.MaterialResistance(35,0,0),0);
        });
        Case("source arrays cannot be modified by solving or result reads", () =>
        {
            double[] u=Enumerable.Range(0,count).Select(i=>(double)(i%5)).ToArray(), copy=(double[])ones.Clone(), uc=(double[])u.Clone();
            var s=LithosphereMembrane.Solve(n,1,1,1,ones,u,zero);
            Require(ones.SequenceEqual(copy)&&u.SequenceEqual(uc),"input changed");
            bool refused=false;try{((IList<double>)s.East)[0]=99;}catch(NotSupportedException){refused=true;}Require(refused,"result mutable");
        });
        Case("iteration exhaustion refuses an unsolved field", () =>
        {
            double[] u=Enumerable.Range(0,count).Select(i=>Math.Sin(i*.19)).ToArray();
            Refuse(()=>LithosphereMembrane.Solve(n,1,1,5,ones,u,zero,1));
        });
        Case("invalid rheology and nonfinite geometry are refused", () =>
        {
            Refuse(()=>LithosphereMembrane.Solve(n,0,1,2,ones,zero,zero));
            Refuse(()=>LithosphereMembrane.Solve(n,1,1,2,zero,zero,zero));
            Refuse(()=>LithosphereMembrane.MaterialResistance(0,0,0));
            Refuse(()=>LithosphereMembrane.MaterialResistance(35,0,double.NaN));
        });
        Case("coarse-to-fine face coupling preserves common translation and volume", () =>
        {
            var state=MaterialPlateCohorts.Create(n,1,new int[count],Enumerable.Repeat(35d,count).ToArray(),zero,zero,zero);
            var frame=MembraneCoupling.Evaluate(state,new[]{new TectonicPlate(0,0,0,2,-1)},1000,1000,1000,8);
            Require(frame.SolveSide==8 && frame.MaterialSide==16,"hidden resolution");
            foreach(double value in frame.East)Near(value,2,1e-9);
            foreach(double value in frame.South)Near(value,-1,1e-9);
            var moved=state.Advect(frame.East.ToArray(),frame.South.ToArray(),1000,1000,1);
            Near(moved.ContinentalInventories()[0],state.ContinentalInventories()[0],1e-7);
            foreach(double value in frame.Divergence)Near(value,0,1e-12);
        });
        Case("membrane histories retain their exact initial assemblage and scale", () =>
        {
            var scale=new TectonicScalePlan(1_000_000,1_000_000);var settings=new TectonicEvolutionSettings(side:32,duration:1);
            var a=ContinentalAssemblage.Generate(73,scale,32,12);
            var old=MaterialBoundHistory.GenerateWithAssemblage(73,scale,settings,a);
            var mechanical=MaterialBoundHistory.GenerateWithMembrane(73,scale,settings,a,32);
            Require(old.Initial.Checksum==mechanical.Initial.Checksum,"initial state changed");
            Require(old.Checksum!=mechanical.Checksum && mechanical.FinalMembrane is not null,"algorithm identity missing");
            Near(old.Ledger[0].ContinentalVolume,mechanical.Ledger[^1].ContinentalVolume,1e-4);
            foreach(long size in new[]{131072L,262144L,1000000L})
            { var small=new TectonicScalePlan(size,size);Near(mechanical.SampleElevationKm(small,.3*size,.7*size),mechanical.SampleElevationKm(scale,300000,700000),1e-10); }
        });
        return cases.ToArray();
        double[] Transpose(IReadOnlyList<double> a)=>Enumerable.Range(0,count).Select(i=>a[i%n*n+i/n]).ToArray();
        void Case(string label,Action test){test();cases.Add(label);Console.WriteLine("CHECK PASS: "+label);}
    }
    static void Near(double a,double b,double e){if(!double.IsFinite(a)||!double.IsFinite(b)||Math.Abs(a-b)>e)throw new InvalidOperationException($"{a:R}!={b:R} tol={e:R}");}
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Refuse(Action a){bool refused=false;try{a();}catch(ArgumentException){refused=true;}catch(ArithmeticException){refused=true;}Require(refused,"invalid input/result accepted");}
    static double[] Gaussian(double[,] a,double[] b)
    {
        int n=b.Length;for(int k=0;k<n;k++)
        {int best=k;for(int i=k+1;i<n;i++)if(Math.Abs(a[i,k])>Math.Abs(a[best,k]))best=i;
         for(int j=k;j<n;j++)(a[k,j],a[best,j])=(a[best,j],a[k,j]);(b[k],b[best])=(b[best],b[k]);
         for(int i=k+1;i<n;i++){double f=a[i,k]/a[k,k];for(int j=k;j<n;j++)a[i,j]-=f*a[k,j];b[i]-=f*b[k];}}
        var x=new double[n];for(int i=n-1;i>=0;i--){double s=b[i];for(int j=i+1;j<n;j++)s-=a[i,j]*x[j];x[i]=s/a[i,i];}return x;
    }
}
