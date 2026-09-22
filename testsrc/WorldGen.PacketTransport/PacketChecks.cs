using ISRWorldGen.Core.Geology.Evolution;

internal static class PacketChecks
{
    public static string[] Run()
    {
        var checks = new List<string>();
        Case("zero step returns owned exact arrays including subnormal carriers", () => {
            double[] q = [double.Epsilon, 2, 3, 4], m = q.Select(v => 50*v).ToArray();
            var r = CrustPacketTransport.Advect(q,m,q,new double[4],new double[4],2,1,1,0);
            Require(q.SequenceEqual(r.Carrier) && m.SequenceEqual(r.Moment),"zero step changed values");
            r.Carrier[0]=1; Require(q[0]==double.Epsilon,"input escaped");
        });
        Case("constant field and constant age survive common translation", () => {
            const int n=16; var q=Enumerable.Repeat(7d,n*n).ToArray(); var m=q.Select(v=>50*v).ToArray();
            var r=CrustPacketTransport.Advect(q,m,q,Enumerable.Repeat(.4,n*n).ToArray(),Enumerable.Repeat(-.2,n*n).ToArray(),n,1,2,.4);
            for(int i=0;i<q.Length;i++) { Near(r.Carrier[i],7,1e-12); Near(r.Moment[i],350,1e-11); Near(r.Inherited[i],r.Carrier[i],0); }
        });
        Case("the old independent stencil reproduces the orphan-age counterexample", () => {
            double[] q=[double.Epsilon,0,0,0], m=[50*double.Epsilon,0,0,0], v=[.125,.125,.125,.125];
            Require(CrustTransport.Advect(q,v,new double[4],2,1,1,1)[1]==0 &&
                CrustTransport.Advect(m,v,new double[4],2,1,1,1)[1]>0,"counterexample disappeared");
        });
        Case("normal and subnormal ocean packets never leave an unsupported age", () => {
            foreach(double small in new[]{double.Epsilon,8*double.Epsilon,1e-310,1e-200,1e-20,7d}) {
                double[] q=[small,0,0,0], m=[50*small,0,0,0], h=(double[])q.Clone(), v=[.125,.125,.125,.125];
                for(int step=0;step<20;step++) {
                    var r=CrustPacketTransport.Advect(q,m,h,v,new double[4],2,1,1,1);
                    q=r.Carrier;m=r.Moment;h=r.Inherited;
                    for(int i=0;i<4;i++) Require(double.IsFinite(m[i]) && h[i]<=q[i] && (q[i]>0 || m[i]==0),"unsupported tracer");
                }
            }
        });
        Case("positive carrier and bounded donor concentrations under divergent forcing", () => {
            const int n=16; var q=Enumerable.Range(0,n*n).Select(i=>1+.8*Math.Sin(i*.37)).ToArray();
            var m=q.Select((v,i)=>v*(20+10*Math.Cos(i*.1))).ToArray(); var h=q.Select(v=>.3*v).ToArray();
            var east=Enumerable.Range(0,n*n).Select(i=>.4*Math.Sin(i*.7)).ToArray();var south=east.Select(v=>-v).ToArray();
            var r=CrustPacketTransport.Advect(q,m,h,east,south,n,1,2,.2);
            for(int i=0;i<q.Length;i++) Require(r.Carrier[i]>0 && r.Moment[i]/r.Carrier[i]>=10-1e-12 && r.Moment[i]/r.Carrier[i]<=30+1e-12,"invented concentration");
            Near(CrustTransport.Sum(r.Carrier),CrustTransport.Sum(q),1e-10);
            Near(CrustTransport.Sum(r.Moment),CrustTransport.Sum(m),1e-8);
        });
        Case("reconstruction reduces smooth translation error relative to donor", () => {
            var a=Translation(32); Console.WriteLine($"TRANSLATION n32 donor={a.Donor:R} packet={a.Packet:R}");
            Require(a.Packet<a.Donor*.3,"no measurable transport improvement");
        });
        Case("smooth-carrier error decreases with refinement", () => {
            var a=Translation(32);var b=Translation(64);Console.WriteLine($"REFINEMENT packet32={a.Packet:R} packet64={b.Packet:R} ratio={a.Packet/b.Packet:R}");
            Require(a.Packet/b.Packet>2.5,"no second-order tendency on smooth carrier");
        });
        Case("sharp translated carrier has no negative values or overshoot", () => {
            const int n=32; double[] q=Enumerable.Range(0,n*n).Select(i=>i%n is >=8 and <20?1d:0d).ToArray();
            var e=Enumerable.Repeat(1d,n*n).ToArray();var s=new double[n*n];
            for(int j=0;j<100;j++)q=CrustPacketTransport.AdvectScalar(q,e,s,n,1,1,.2);
            Require(q.Min()>=0 && q.Max()<=1+1e-12,"new extremum in pure translation");
            Near(CrustTransport.Sum(q),12*n,1e-9);
        });
        Case("axis exchange and periodic shifts agree", () => {
            const int n=16;var q=Enumerable.Range(0,n*n).Select(i=>1+.3*Math.Sin(i*.37)).ToArray();
            double[] Transpose(double[] a)=>Enumerable.Range(0,n*n).Select(i=>a[(i%n)*n+i/n]).ToArray();
            double[] Shift(double[] a)=>Enumerable.Range(0,n*n).Select(i=>a[(i/n)*n+(i%n+n-1)%n]).ToArray();
            var e=Enumerable.Repeat(.3,n*n).ToArray();var s=Enumerable.Repeat(-.1,n*n).ToArray();
            var a=CrustPacketTransport.AdvectScalar(q,e,s,n,1,2,.2);
            var b=Transpose(CrustPacketTransport.AdvectScalar(Transpose(q),Transpose(s),Transpose(e),n,2,1,.2));
            var c=CrustPacketTransport.AdvectScalar(Shift(q),e,s,n,1,2,.2);
            for(int i=0;i<q.Length;i++){Near(a[i],b[i],1e-12);Near(Shift(a)[i],c[i],1e-12);}
        });
        Case("invalid CFL is refused without mutating material", () => {
            double[] q=[1,2,3,4], old=(double[])q.Clone();Refuse(()=>CrustPacketTransport.AdvectScalar(q,[1,1,1,1],new double[4],2,1,1,.6));
            Require(q.SequenceEqual(old),"input mutated after refusal");
        });
        Case("missing negative and nonfinite material or geometry is refused", () => {
            Refuse(()=>CrustPacketTransport.AdvectScalar([1,-1,1,1],new double[4],new double[4],2,1,1,.1));
            Refuse(()=>CrustPacketTransport.AdvectScalar(new double[4],new double[4],new double[4],2,double.NaN,1,.1));
            Refuse(()=>CrustPacketTransport.AdvectScalar(new double[4],new double[3],new double[4],2,1,1,.1));
            Refuse(()=>CrustPacketTransport.Advect(new double[4],[1,0,0,0],new double[4],new double[4],new double[4],2,1,1,.1));
        });
        Case("per-origin reconstruction keeps inventories separate", () => {
            const int n=8; int[] owners=Enumerable.Range(0,n*n).Select(i=>i%n<4?0:1).ToArray();
            var q=Enumerable.Repeat(35d,n*n).ToArray();var o=Enumerable.Repeat(7d,n*n).ToArray();
            var a=MaterialPlateCohorts.Create(n,2,owners,q,o,o.Select(v=>50*v).ToArray(),o);
            var b=a.AdvectLimitedPackets(Enumerable.Repeat(.2,n*n).ToArray(),new double[n*n],1,1,.5);
            for(int p=0;p<2;p++)Near(a.ContinentalInventories()[p],b.ContinentalInventories()[p],1e-10);
        });
        Case("whole-history opt-in changes identity but never its initial materials", () => {
            var scale=new TectonicScalePlan(1000000,1000000); var settings=new TectonicEvolutionSettings(side:32,duration:1);
            var a=ContinentalAssemblage.Generate(73,scale,32);
            var old=MaterialBoundHistory.GenerateWithAssemblage(73,scale,settings,a);
            var next=MaterialBoundHistory.GenerateWithLimitedPackets(73,scale,settings,a);
            Require(old.Initial.Checksum==next.Initial.Checksum && old.Checksum!=next.Checksum,"invalid causal identity");
            Require(next.TransportPolicy==CrustPacketTransport.AlgorithmId,"transport identity absent");
            foreach(long size in new long[]{131072,262144,1000000})Near(next.SampleElevationKm(new(size,size),size*.375,size*.625),next.SampleElevationKm(scale,375000,625000),1e-12);
        });
        return checks.ToArray();
        void Case(string label,Action run){run();checks.Add(label);Console.WriteLine("CHECK PASS: "+label);}
    }
    private static (double Donor,double Packet) Translation(int n)
    {
        int steps=5*n;double dx=1d/n,dt=1d/steps;
        double[] initial=Enumerable.Range(0,n*n).Select(i=>1+.4*Math.Sin(Math.Tau*(i%n+.5)/n)).ToArray();
        double[] q=(double[])initial.Clone(),r=(double[])initial.Clone(),e=Enumerable.Repeat(1d,n*n).ToArray(),s=new double[n*n];
        for(int j=0;j<steps;j++) {q=CrustTransport.Advect(q,e,s,n,dx,dx,dt);r=CrustPacketTransport.AdvectScalar(r,e,s,n,dx,dx,dt);}
        return (q.Select((v,i)=>Math.Abs(v-initial[i])).Average(),r.Select((v,i)=>Math.Abs(v-initial[i])).Average());
    }
    private static void Require(bool valid,string msg){if(!valid)throw new InvalidOperationException(msg);}
    private static void Near(double a,double b,double tolerance){Require(double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=tolerance,$"{a:R} != {b:R}");}
    private static void Refuse(Action run){try{run();}catch(ArgumentException){return;}throw new InvalidOperationException("Invalid input accepted");}
}
