using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Experimental constitutive values, NOT an Earth calibration.
/// ResidualRatio=1 is the strict memory-disabled mechanical control.
/// The metric support regularizes rheology, never terrain or material volumes.</summary>
public sealed record StrainWeakeningOptions
{
    public int MechanicalSide { get; }
    public double UpdateInterval { get; }
    public double StrainScale { get; }
    public double ResidualRatio { get; }
    public double SupportLengthReference { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SheetYieldOptions? Yield { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LithostaticReliefOptions? Gravity { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PlateDrivingSchedule? Driving { get; }
    public StrainWeakeningOptions(int mechanicalSide = 128, double updateInterval = 1,
        double strainScale = .5, double residualRatio = .35, double supportLengthReference = 10000, SheetYieldOptions? yieldOptions = null, LithostaticReliefOptions? gravity = null, PlateDrivingSchedule? driving = null)
    {
        if (mechanicalSide < 8 || mechanicalSide > 128 || (mechanicalSide & (mechanicalSide - 1)) != 0 ||
            !double.IsFinite(updateInterval) || updateInterval < .125 || updateInterval > 1 ||
            !double.IsFinite(strainScale) || strainScale < .05 || strainScale > 10 ||
            !double.IsFinite(residualRatio) || residualRatio < .1 || residualRatio > 1 ||
            !double.IsFinite(supportLengthReference) || supportLengthReference < 0 || supportLengthReference > 80000)
            throw new ArgumentException("Invalid bounded strain weakening parameters.");
        MechanicalSide = mechanicalSide; UpdateInterval = updateInterval; StrainScale = strainScale;
        ResidualRatio = residualRatio; SupportLengthReference = supportLengthReference; Yield = yieldOptions; Gravity = gravity; Driving = driving;
    }
    public double Multiplier(double strain)
    {
        if (!double.IsFinite(strain) || strain < 0) throw new ArgumentException("Invalid accumulated viscous strain.");
        return ResidualRatio + (1 - ResidualRatio) * Math.Exp(-strain / StrainScale);
    }
}

public sealed record WeakeningSolveReceipt(double Time, int MechanicalSide, int Iterations,
    double RelativeResidual, double Work, double Dissipation, double MeanVelocityError,
    double MinimumMultiplier, double MaximumStrain)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int NonlinearIterations { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double YieldedQuadratureFraction { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double ConstitutiveResidual { get; init; }
}

public sealed record StrainWeakeningResult(MaterialBoundHistory History, ContinentalStrainMemory Memory,
    ReadOnlyCollection<WeakeningSolveReceipt> Solves, StrainWeakeningOptions Options, string Policy)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public WeakeningFrame? LastFrame { get; init; }
}

/// <summary>Current force-balanced frame. Rates/transport on fine grid are sampled
/// from a native staggered mechanical grid, not new solved detail.</summary>
public sealed class WeakeningFrame
{
    public ThinSheetSolution Native { get; }
    public int NativeSide { get; }
    public ReadOnlyCollection<double> Viscosity { get; }
    public ReadOnlyCollection<double> Multipliers { get; }
    public ReadOnlyCollection<double> EffectiveStrain { get; }
    public double[] East { get; }
    public double[] South { get; }
    public double[] Divergence { get; }
    public double[] Rates { get; }
    public double[] CenterX { get; }
    public double[] CenterZ { get; }
    public double[] Shear { get; }
    public ViscoplasticSheetSolution? YieldSolution { get; }
    public double[]? PlasticRates { get; }
    private readonly int n;
    private readonly double dx, dz;
    internal WeakeningFrame(ThinSheetSolution solved, int m, double[] viscosity, double[] multiplier,
        double[] strain, int n, double dx, double dz, ViscoplasticSheetSolution? yieldSolution = null)
    {
        Native = solved; NativeSide = m;
        YieldSolution = yieldSolution;
        if (yieldSolution is not null)
        {
            // Cell-quadrature rate projected as piecewise constant subcells.
            // This does NOT claim material-grid resolution for mechanical strain.
            PlasticRates = new double[n * n]; int ratio0 = n / m;
            for (int z0=0; z0<n; z0++) for(int x0=0; x0<n; x0++)
                PlasticRates[z0*n+x0] = yieldSolution.PlasticRates[(z0/ratio0)*m+x0/ratio0];
        } Viscosity = Array.AsReadOnly(viscosity);
        Multipliers = Array.AsReadOnly(multiplier); EffectiveStrain = Array.AsReadOnly(strain);
        this.n = n; this.dx = dx; this.dz = dz;
        East = new double[n*n]; South = new double[n*n]; Divergence = new double[n*n];
        Rates = new double[n*n]; CenterX = new double[n*n]; CenterZ = new double[n*n]; Shear = new double[n*n];
        double ratio = (double)n / m;
        for (int z=0; z<n; z++) for (int x=0; x<n; x++)
        {
            int i=z*n+x;
            East[i]=Sample(solved.East,m,(x+1)/ratio-1,(z+.5)/ratio-.5);
            South[i]=Sample(solved.South,m,(x+.5)/ratio-.5,(z+1)/ratio-1);
        }
        var gamma = new double[n*n];
        for (int z=0; z<n; z++) for (int x=0; x<n; x++)
        {
            int i=z*n+x,e=z*n+(x+1)%n,s=((z+1)%n)*n+x,w=z*n+(x+n-1)%n,no=((z+n-1)%n)*n+x;
            gamma[i]=(East[s]-East[i])/dz+(South[e]-South[i])/dx;
            CenterX[i]=.5*(East[i]+East[w]); CenterZ[i]=.5*(South[i]+South[no]);
        }
        for (int z=0; z<n; z++) for (int x=0; x<n; x++)
        {
            int i=z*n+x,w=z*n+(x+n-1)%n,no=((z+n-1)%n)*n+x,nw=((z+n-1)%n)*n+(x+n-1)%n;
            double ex=(East[i]-East[w])/dx,ez=(South[i]-South[no])/dz;
            Divergence[i]=ex+ez; Shear[i]=.25*(gamma[i]+gamma[w]+gamma[no]+gamma[nw]);
            // Corner quadrature retains shear energy instead of cancelling
            // opposite engineering shear at the centre of an unresolved cell.
            Rates[i]=Math.Sqrt(.5*(ex*ex+ez*ez+(ex+ez)*(ex+ez))+
                (gamma[i]*gamma[i]+gamma[w]*gamma[w]+gamma[no]*gamma[no]+gamma[nw]*gamma[nw])/16);
        }
    }
    public double ClosingSpeed(int cell, bool eastFace, double nx, double nz, double distance)
    {
        double x=(cell%n+.5+(eastFace ? .5:0))*dx,z=(cell/n+.5+(eastFace?0:.5))*dz;
        double ax=(x-nx*distance)/dx-.5,az=(z-nz*distance)/dz-.5;
        double bx=(x+nx*distance)/dx-.5,bz=(z+nz*distance)/dz-.5;
        return (Sample(CenterX,n,ax,az)-Sample(CenterX,n,bx,bz))*nx+
               (Sample(CenterZ,n,ax,az)-Sample(CenterZ,n,bx,bz))*nz;
    }
    internal static double Sample(IReadOnlyList<double> a,int side,double x,double z)
    {
        int ix=(int)Math.Floor(x),iz=(int)Math.Floor(z); double tx=x-ix,tz=z-iz;
        int x0=(ix%side+side)%side,z0=(iz%side+side)%side,x1=(x0+1)%side,z1=(z0+1)%side;
        return (1-tz)*((1-tx)*a[z0*side+x0]+tx*a[z0*side+x1])+tz*((1-tx)*a[z1*side+x0]+tx*a[z1*side+x1]);
    }
}

public static class StrainWeakeningRheology
{
    public const string AlgorithmId = "advected-viscous-strain-continental-softening-sheet-v1";
    public static double Viscosity(double c,double o,double age,double multiplier)
    {
        if (!double.IsFinite(c)||c<0||!double.IsFinite(o)||o<0||!(c+o>0)||!double.IsFinite(c+o)||
            !double.IsFinite(age)||age<0||!double.IsFinite(multiplier)||multiplier<=0||multiplier>1)
            throw new ArgumentException("Invalid rheology column.");
        // Same declared composition/age PRIOR as MaterialRheology on the
        // rheology branch. Only the continental component is strain-weakened.
        double f=c/(c+o),continent=(2+2*c/(c+35))*multiplier,ocean=.25+1.75*age/(age+40);
        return 1/(f/continent+(1-f)/ocean);
    }
    public static WeakeningFrame Solve(MaterialPlateCohorts state,IReadOnlyList<TectonicPlate> plates,
        ContinentalStrainMemory memory,double dx,double dz,double coupling,StrainWeakeningOptions options,
        ThinSheetSolution? warmStart=null)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(memory); ArgumentNullException.ThrowIfNull(options);
        int n=state.Side,m=Math.Min(n,options.MechanicalSide),ratio=n/m,count=m*m;
        if (n%m!=0||memory.Side!=n||plates.Count!=state.PlateCount||plates.Where((p,i)=>p.Id!=i||!double.IsFinite(p.Vx)||!double.IsFinite(p.Vz)).Any()||
            !double.IsFinite(dx)||dx<=0||!double.IsFinite(dz)||dz<=0||options.SupportLengthReference>Math.Min(n*dx,n*dz))
            throw new ArgumentException("Incompatible mechanical and material state.");
        var c=new double[count]; var o=new double[count]; var age=new double[count];var q=new double[count];var mc=new double[count];
        var vx=new double[count];var vz=new double[count];var continent=new double[n*n];
        for (int z=0;z<n;z++) for (int x=0;x<n;x++)
        {
            int i=z*n+x,k=(z/ratio)*m+x/ratio;
            q[k]+=memory.Moment[i];mc[k]+=memory.Carrier[i];
            for (int p=0;p<plates.Count;p++)
            {
                double ci=state.Value(p,0,i),oi=state.Value(p,1,i);continent[i]+=ci;
                c[k]+=ci;o[k]+=oi;age[k]+=state.Value(p,2,i);
                vx[k]+=(ci+oi)*plates[p].Vx;vz[k]+=(ci+oi)*plates[p].Vz;
            }
        }
        memory.RequireCarrier(continent);
        // Nonlocal material-weighted sampling with a fixed reference length.
        // No per-world percentile or height-dependent tuning.
        (mc,q)=SampleSupport(mc,q,m,dx*ratio,dz*ratio,options.SupportLengthReference);
        var mu=new double[count];var mult=new double[count];var strain=new double[count];
        for (int i=0;i<count;i++)
        {
            if (!(c[i]+o[i]>0)) throw new ArithmeticException("Missing material forcing.");
            vx[i]/=c[i]+o[i];vz[i]/=c[i]+o[i];
            if (mc[i]<0||q[i]<0) throw new ArithmeticException("Nonlocal memory support became negative.");
            strain[i]=mc[i]>0?q[i]/mc[i]:0;
            if (mc[i]==0&&q[i]!=0) throw new ArithmeticException("Nonlocal memory has no carrier.");
            mult[i]=options.Multiplier(strain[i]);
            mu[i]=Viscosity(c[i]/(ratio*ratio),o[i]/(ratio*ratio),o[i]>0?age[i]/o[i]:0,options.Yield is null ? mult[i] : 1);
        }
        var east=new double[count];var south=new double[count];
        for(int z=0;z<m;z++)for(int x=0;x<m;x++){int i=z*m+x;east[i]=.5*(vx[i]+vx[z*m+(x+1)%m]);south[i]=.5*(vz[i]+vz[((z+1)%m)*m+x]);}
        if (options.Gravity is { Mobility: > 0 } gravity)
        {
            double[] potential = new double[count];
            // Average column potential from resolved material cells. Computing
            // potential only from averaged thickness would lose sub-grid load
            // variance (Jensen bias) before the mechanical solve.
            for (int z=0;z<n;z++) for (int x=0;x<n;x++)
            {
                int i=z*n+x,k=(z/ratio)*m+x/ratio;
                double ci=0,oi=0,ai=0;
                for (int p=0;p<plates.Count;p++)
                { ci+=state.Value(p,0,i);oi+=state.Value(p,1,i);ai+=state.Value(p,2,i); }
                potential[k]+=LithostaticRelief.Column(ci,oi,oi>0?ai/oi:0,
                    gravity.FinitePlateCooling).PotentialKm2/(ratio*ratio);
            }
            var body=LithostaticRelief.Forcing(potential,m,dx*ratio,dz*ratio,gravity.Mobility);
            for(int i=0;i<count;i++) { east[i]+=body.East[i];south[i]+=body.South[i]; }
        }
        ViscoplasticSheetSolution? plastic = null;
        if (options.Yield is { } y)
        {
            // A declared material mixture PRIOR, not a mass ranking or a
            // pressure-derived yield envelope. Only the continental load is weakened.
            double[] loads = Enumerable.Range(0,count).Select(i =>
                (c[i] * y.ContinentalLoad * mult[i] + o[i] * y.OceanicLoad) / (c[i]+o[i])).ToArray();
            plastic = ViscoplasticSheet.Solve(east,south,mu,loads,m,dx*ratio,dz*ratio,coupling,y,warmStart);
        }
        var solved=plastic?.Velocity ?? ThinSheetDeformation.Solve(east,south,mu,m,dx*ratio,dz*ratio,coupling,warmStart:warmStart);
        return new(solved,m,mu,mult,strain,n,dx,dz,plastic);
    }
    // Positive packet quadrature instead of subtractive sliding sums. This
    // constitutive sampling has a finite metric support and does not modify the
    // transported carrier or moment. Normalized packet weights keep zero
    // carrier paired with zero memory, including subnormal quantities.
    private static (double[] C,double[] Q) SampleSupport(double[] c,double[] q,int n,double dx,double dz,double length)
    {
        for(int pass=0;pass<3;pass++) { (c,q)=Axis(c,q,length/dx,false);(c,q)=Axis(c,q,length/dz,true); }
        return(c,q);
        (double[],double[]) Axis(double[] a,double[] b,double half,bool vertical)
        {
            if(half<=.5)return(a,b);
            int radius=(int)Math.Floor(half-.5);double edge=half-radius-.5,total=2*half;
            var nc=new double[a.Length];var nq=new double[a.Length];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                int i=z*n+x;
                for(int offset=-radius-1;offset<=radius+1;offset++)
                {
                    double weight=Math.Abs(offset)<=radius?1:edge;
                    int xx=vertical?x:(x+offset%n+n)%n,zz=vertical?(z+offset%n+n)%n:z;
                    int j=zz*n+xx;
                    if(a[j]==0)continue;
                    double packet=a[j]*(weight/total);
                    nc[i]+=packet;nq[i]+=packet*(b[j]/a[j]);
                }
            }
            return(nc,nq);
        }
    }
    public static double StableStep(WeakeningFrame frame,int side,double dx,double dz)
    {
        double rate=0;
        for(int z=0;z<side;z++)for(int x=0;x<side;x++)
        {
            int i=z*side+x,w=z*side+(x+side-1)%side,no=((z+side-1)%side)*side+x;
            rate=Math.Max(rate,(Math.Max(frame.East[i],0)+Math.Max(-frame.East[w],0))/dx+
                (Math.Max(frame.South[i],0)+Math.Max(-frame.South[no],0))/dz);
        }
        return rate > 0 ? .35/rate:1;
    }
}

internal sealed class WeakeningDriver
{
    internal StrainWeakeningOptions Options { get; }
    internal ContinentalStrainMemory Memory { get; private set; } = null!;
    internal WeakeningFrame? Frame { get; private set; }
    internal double NextSolveTime { get; private set; }
    private readonly List<WeakeningSolveReceipt> receipts=new();
    internal string Policy => (Options.Yield is null
        ? StrainWeakeningRheology.AlgorithmId+"/"+ThinSheetDeformation.AlgorithmId
        : "transported-plastic-excess-yield-weakening-v1/"+ViscoplasticSheet.AlgorithmId)+"/"+JsonSerializer.Serialize(Options);
    internal WeakeningDriver(StrainWeakeningOptions options) { Options=options; }
    internal void Initialize(int n,double[] c) { Memory=new(n,c,kind:Options.Yield is null ? ContinentalStrainKind.Viscous : ContinentalStrainKind.PlasticExcess); }
    internal void Prepare(double time,MaterialPlateCohorts state,IReadOnlyList<TectonicPlate> plates,double dx,double dz,double length)
    {
        if(Frame is not null&&time<NextSolveTime)return;
        Frame=StrainWeakeningRheology.Solve(state,plates,Memory,dx,dz,length,Options,Frame?.Native);
        NextSolveTime=time+Options.UpdateInterval;
        var f=Frame;var s=f.Native;
        receipts.Add(new(time,f.NativeSide,s.Iterations,s.RelativeResidual,s.Work,s.Dissipation,s.MeanVelocityError,
            f.Multipliers.Min(),f.EffectiveStrain.Max())
        {
            NonlinearIterations=f.YieldSolution?.NonlinearIterations ?? 0,
            YieldedQuadratureFraction=f.YieldSolution?.YieldedFraction.Average() ?? 0,
            ConstitutiveResidual=f.YieldSolution?.MaximumConstitutiveResidual ?? 0
        });
    }
    internal void Advance(double dt,double dx,double dz,double mobility,double[] advected,double[] final)
    {
        var f=Frame!;
        var next=Memory.Accumulate(f.PlasticRates ?? f.Rates,dt).Advect(f.East,f.South,dx,dz,dt);
        next.RequireCarrier(advected);next=next.Relax(advected,dx,dz,dt,mobility);next.RequireCarrier(final);Memory=next;
    }
    internal StrainWeakeningResult Result(MaterialBoundHistory history)=>new(history,Memory,receipts.AsReadOnly(),Options,Policy) { LastFrame=Frame };
}
