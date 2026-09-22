using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>F maps initial material line elements to their current configuration.
/// This is kinematics, not a stress law or a fracture oracle.</summary>
public readonly record struct Deformation2D(double XX, double XZ, double ZX, double ZZ)
{
    public static Deformation2D Identity => new(1, 0, 0, 1);
    public double AreaRatio => XX * ZZ - XZ * ZX;
    public bool IsFinite => double.IsFinite(XX) && double.IsFinite(XZ) && double.IsFinite(ZX) && double.IsFinite(ZZ);
    public static Deformation2D operator *(Deformation2D a, Deformation2D b) => new(
        a.XX * b.XX + a.XZ * b.ZX, a.XX * b.XZ + a.XZ * b.ZZ,
        a.ZX * b.XX + a.ZZ * b.ZX, a.ZX * b.XZ + a.ZZ * b.ZZ);
    private static Deformation2D Add(Deformation2D a, Deformation2D b) => new(a.XX+b.XX,a.XZ+b.XZ,a.ZX+b.ZX,a.ZZ+b.ZZ);
    private Deformation2D Scale(double s) => new(XX*s,XZ*s,ZX*s,ZZ*s);

    /// <summary>exp(dt*L)*F for a frozen velocity gradient. Scaling and squaring
    /// avoids eigenvector singularities at zero strain and rigid rotation.</summary>
    public Deformation2D Advance(Deformation2D velocityGradient, double dt)
    {
        if (!IsFinite || !(AreaRatio > 0) || !velocityGradient.IsFinite || !double.IsFinite(dt) || dt < 0)
            throw new ArgumentException("Invalid material deformation or time interval.");
        var a = velocityGradient.Scale(dt);
        double norm = Math.Max(Math.Abs(a.XX)+Math.Abs(a.XZ),Math.Abs(a.ZX)+Math.Abs(a.ZZ));
        if (!double.IsFinite(norm) || norm > 64) throw new ArgumentException("Unresolved deformation step; subdivide the physical interval.");
        int squares = 0;
        while (norm > .5) { a=a.Scale(.5); norm *= .5; squares++; }
        var e = Identity; var term = Identity;
        for (int k=1;k<=24;k++) { term=(term*a).Scale(1d/k); e=Add(e,term); }
        for (int k=0;k<squares;k++) e=e*e;
        var result=e*this;
        double expected=AreaRatio*Math.Exp(dt*(velocityGradient.XX+velocityGradient.ZZ));
        if (!result.IsFinite || !(result.AreaRatio > 0) || !double.IsFinite(expected) ||
            Math.Abs(result.AreaRatio-expected) > 1e-9*Math.Max(expected,1e-100))
            throw new ArithmeticException("Material Jacobian is unresolved; no clipping or repair.");
        return result;
    }

    /// <summary>Current spatial principal stretching direction from F*F^T.
    /// Sign is only a reporting convention; n and -n represent the same axis.
    /// Equal stretches return no direction, never a grid-aligned default.</summary>
    public (double Major, double Minor, bool HasDirection, double NormalX, double NormalZ) Principal()
    {
        if (!IsFinite || !(AreaRatio > 0)) throw new ArgumentException("Invalid deformation tensor.");
        double a=XX*XX+XZ*XZ, b=XX*ZX+XZ*ZZ, d=ZX*ZX+ZZ*ZZ;
        double gap=Math.Sqrt((a-d)*(a-d)+4*b*b), largest=.5*(a+d+gap);
        double major=Math.Sqrt(largest), minor=AreaRatio/major;
        if (!double.IsFinite(major) || !(minor > 0) || major/minor > 1e8)
            throw new ArithmeticException("Unresolved principal stretches.");
        if (gap <= 1e-8*(a+d)) return (major,minor,false,0,0);
        double nx=a>=d ? a-d+gap : 2*b, nz=a>=d ? 2*b : d-a+gap;
        double length=Math.Sqrt(nx*nx+nz*nz); nx/=length; nz/=length;
        if (nx<0 || (nx==0 && nz<0)) { nx=-nx; nz=-nz; }
        return (major,minor,true,nx,nz);
    }
}

/// <summary>Diagnostic thresholds supplied explicitly. They are NOT geological
/// breakup criteria; crossing them never creates ocean or deletes continent.</summary>
public sealed record MaterialStrainOptions(int MarkerSide=128, double AreaThreshold=2,
    double MinimumInitialContinentalKm=10, double MinimumInitialContinentalFraction=.8)
{
    internal void Validate(int nativeSide)
    {
        if (MarkerSide is < 8 or > 256 || (MarkerSide & (MarkerSide-1)) != 0 || MarkerSide>nativeSide ||
            !double.IsFinite(AreaThreshold) || AreaThreshold<=1 || AreaThreshold>10 ||
            !double.IsFinite(MinimumInitialContinentalKm) || MinimumInitialContinentalKm<0 ||
            !double.IsFinite(MinimumInitialContinentalFraction) || MinimumInitialContinentalFraction<0 || MinimumInitialContinentalFraction>1)
            throw new ArgumentException("Invalid bounded material-strain diagnostic settings.");
    }
}

public sealed record MaterialStrainSample(int Id, double InitialX, double InitialZ,
    double CurrentX, double CurrentZ, double InitialContinentalKm, double InitialOceanicKm,
    Deformation2D F, double AreaRatio, double MajorStretch, double MinorStretch,
    bool HasDirection, double NormalX, double NormalZ, double AdvectiveThicknessKm,
    double FirstAreaThresholdSampleTime, bool ContinentalThinningCandidate);

public sealed record MaterialStrainSnapshot(string Algorithm, double TimeMyr, double ReferenceWidth,
    double ReferenceLength, MaterialStrainOptions Options, ReadOnlyCollection<MaterialStrainSample> Samples);

/// <summary>
/// Passive Lagrangian markers integrated through the ACTUAL cell-centred velocity
/// field used by MaterialBoundHistory. Fdot=L*F, Xdot=v. RK2 trajectories and
/// exponential-midpoint deformation within each frozen solver step. The gradient
/// is the derivative of the same periodic bilinear interpolant as the velocity.
///
/// No force is inferred, no threshold cuts a plate and no material is created.
/// h0/det(F) describes incompressible ADVECTIVE thinning only: lower-crust flow,
/// Eulerian donor diffusion and mixing are not silently folded into that estimate.
/// Samples retain initial IDs/coordinates; their raster is a REFERENCE map, not
/// an Eulerian heightmap. Current coordinates must accompany spatial overlays.
/// </summary>
public sealed class MaterialStrainTracker
{
    public const string AlgorithmId="lagrangian-finite-strain-exponential-midpoint-v1";
    private readonly int nativeSide;
    private readonly double width,length,dx,dz;
    private readonly MaterialStrainOptions options;
    private double[] x,z,firstCrossing;
    private readonly double[] initialX,initialZ,initialC,initialO;
    private Deformation2D[] f;
    public double TimeMyr { get; private set; }

    public MaterialStrainTracker(int nativeSide,double width,double length,IReadOnlyList<double> continental,
        IReadOnlyList<double> oceanic,MaterialStrainOptions options)
    {
        ArgumentNullException.ThrowIfNull(continental); ArgumentNullException.ThrowIfNull(oceanic); ArgumentNullException.ThrowIfNull(options);
        if (nativeSide is < 8 or > 512 || !double.IsFinite(width) || !double.IsFinite(length) || width<=0 || length<=0 ||
            continental.Count!=nativeSide*nativeSide || oceanic.Count!=continental.Count ||
            continental.Any(v=>!double.IsFinite(v)||v<0) || oceanic.Any(v=>!double.IsFinite(v)||v<0))
            throw new ArgumentException("Invalid full-world material grid.");
        options.Validate(nativeSide);
        this.nativeSide=nativeSide; this.width=width; this.length=length; this.options=options;
        dx=width/nativeSide; dz=length/nativeSide;
        if (!(dx>0) || !(dz>0)) throw new ArgumentException("Unrepresentable metric.");
        int count=options.MarkerSide*options.MarkerSide;
        x=new double[count]; z=new double[count]; f=new Deformation2D[count];
        firstCrossing=Enumerable.Repeat(-1d,count).ToArray();
        initialC=new double[count]; initialO=new double[count];
        for (int i=0;i<count;i++)
        {
            x[i]=(i%options.MarkerSide+.5)*width/options.MarkerSide;
            z[i]=(i/options.MarkerSide+.5)*length/options.MarkerSide;
            initialC[i]=Interpolate(continental,x[i],z[i]).Value;
            initialO[i]=Interpolate(oceanic,x[i],z[i]).Value;
            if (!(initialC[i]+initialO[i]>0)) throw new ArgumentException("Unresolved empty material marker.");
            f[i]=Deformation2D.Identity;
        }
        initialX=(double[])x.Clone(); initialZ=(double[])z.Clone();
    }

    /// <summary>Transactional in memory: any invalid input or failed substep
    /// leaves time, positions and tensors unchanged. No mutable inputs retained.</summary>
    public void Advance(double startTime,double dt,IReadOnlyList<double> vx,IReadOnlyList<double> vz)
    {
        ArgumentNullException.ThrowIfNull(vx); ArgumentNullException.ThrowIfNull(vz);
        if (startTime!=TimeMyr || !double.IsFinite(dt) || dt<=0 || !double.IsFinite(startTime+dt) || startTime+dt<=startTime ||
            vx.Count!=nativeSide*nativeSide || vz.Count!=vx.Count ||
            vx.Any(v=>!double.IsFinite(v)) || vz.Any(v=>!double.IsFinite(v)))
            throw new ArgumentException("Invalid or nonconsecutive material observation step.");
        double courant=dt*(vx.Max(v=>Math.Abs(v))/dx+vz.Max(v=>Math.Abs(v))/dz);
        if (!double.IsFinite(courant) || courant>12.8) throw new ArgumentException("Marker path exceeds bounded substep budget.");
        int steps=Math.Max(1,(int)Math.Ceiling(courant/.2)); double h=dt/steps;
        double[] nx=(double[])x.Clone(),nz=(double[])z.Clone(),hits=(double[])firstCrossing.Clone();
        var nf=(Deformation2D[])f.Clone();
        for (int step=0;step<steps;step++) for (int i=0;i<x.Length;i++)
        {
            double mx=Wrap(nx[i]+.5*h*Interpolate(vx,nx[i],nz[i]).Value,width);
            double mz=Wrap(nz[i]+.5*h*Interpolate(vz,nx[i],nz[i]).Value,length);
            var u=Interpolate(vx,mx,mz); var v=Interpolate(vz,mx,mz);
            nf[i]=nf[i].Advance(new(u.Dx,u.Dz,v.Dx,v.Dz),h);
            nx[i]=Wrap(nx[i]+h*u.Value,width); nz[i]=Wrap(nz[i]+h*v.Value,length);
            if (hits[i]<0 && nf[i].AreaRatio>options.AreaThreshold*(1+1e-9))
                hits[i]=startTime+(step+1)*h; // first sampled crossing, NOT an exact rupture date
        }
        x=nx; z=nz; f=nf; firstCrossing=hits; TimeMyr=startTime+dt;
    }

    public MaterialStrainSnapshot Snapshot()
    {
        var samples=new MaterialStrainSample[x.Length];
        for (int i=0;i<x.Length;i++)
        {
            var p=f[i].Principal(); double area=f[i].AreaRatio;
            bool continental=initialC[i]>=options.MinimumInitialContinentalKm &&
                initialC[i]/(initialC[i]+initialO[i])>=options.MinimumInitialContinentalFraction;
            bool candidate=continental && area>options.AreaThreshold*(1+1e-9) && p.HasDirection;
            samples[i]=new(i,initialX[i],initialZ[i],x[i],z[i],initialC[i],initialO[i],f[i],area,
                p.Major,p.Minor,p.HasDirection,p.NormalX,p.NormalZ,initialC[i]/area,firstCrossing[i],candidate);
        }
        return new(AlgorithmId,TimeMyr,width,length,options,Array.AsReadOnly(samples));
    }

    private (double Value,double Dx,double Dz) Interpolate(IReadOnlyList<double> a,double px,double pz)
    {
        double gx=Wrap(px,width)/dx-.5,gz=Wrap(pz,length)/dz-.5;
        int ix=(int)Math.Floor(gx),iz=(int)Math.Floor(gz);
        double tx=gx-ix,tz=gz-iz;
        int x0=(ix+nativeSide)%nativeSide,x1=(x0+1)%nativeSide;
        int z0=(iz+nativeSide)%nativeSide,z1=(z0+1)%nativeSide;
        double aa=a[z0*nativeSide+x0],b=a[z0*nativeSide+x1],c=a[z1*nativeSide+x0],d=a[z1*nativeSide+x1];
        return ((1-tz)*((1-tx)*aa+tx*b)+tz*((1-tx)*c+tx*d),
            ((1-tz)*(b-aa)+tz*(d-c))/dx,((1-tx)*(c-aa)+tx*(d-b))/dz);
    }
    private static double Wrap(double p,double span)=>p-Math.Floor(p/span)*span;
}
