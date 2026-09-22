using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Normalized model stresses (relative viscosity / model Myr), not MPa.
/// A Newtonian dashpot in series with a Bingham overstress branch. There is no
/// elastic storage, brittle crack, topological separation or Earth calibration.</summary>
public sealed record PlasticSheetOptions
{
    public double YieldStress { get; }
    public double PostYieldRatio { get; }
    public bool Enabled { get; }
    public PlasticSheetOptions(double yieldStress = .04, double postYieldRatio = .2, bool enabled = true)
    {
        if (!double.IsFinite(yieldStress) || yieldStress <= 0 || yieldStress > 1e6 ||
            !double.IsFinite(postYieldRatio) || postYieldRatio < .1 || postYieldRatio > 1)
            throw new ArgumentException("Invalid normalized yield stress or post-yield tangent ratio.");
        YieldStress = yieldStress; PostYieldRatio = postYieldRatio; Enabled = enabled;
    }
}

public readonly record struct PlasticFlow(double Viscosity, double Stress, double ViscousRate,
    double PlasticRate, double Potential, double ViscosityDerivative);
public sealed record PlasticSheetSolution(ThinSheetSolution Native, ReadOnlyCollection<double> Rates,
    ReadOnlyCollection<double> PlasticRates, ReadOnlyCollection<double> Stress,
    ReadOnlyCollection<double> YieldStress, ReadOnlyCollection<double> EffectiveViscosity,
    int NewtonIterations, double InitialEnergy, double FinalEnergy);

/// <summary>Convex cell-quadrature viscoplastic thin-sheet equilibrium on a periodic
/// MAC grid. Arithmetic corner stresses are DERIVED from the same cell potential;
/// they deliberately differ from the older harmonic-corner linear operator.
/// Both paired controls use this discretization. Newton/CG acceptance checks the
/// actual nonlinear force residual. No fallback, height filter or stress clipping.</summary>
public static class ViscoplasticSheet
{
    public const string AlgorithmId = "convex-mac-viscous-bingham-series-v1";
    public static PlasticFlow Flow(double mu, double rate, double yieldStress, double ratio)
    {
        if (!double.IsFinite(mu) || mu <= 0 || !double.IsFinite(rate) || rate < 0 ||
            !double.IsFinite(yieldStress) || yieldStress <= 0 || !double.IsFinite(ratio) || ratio <= 0 || ratio > 1)
            throw new ArgumentException("Invalid constitutive state.");
        double ey = yieldStress / (2 * mu), plastic = 0, effective = mu, derivative = 0;
        double potential = 2 * mu * rate * rate;
        if (rate > ey && ratio < 1)
        {
            double delta = rate - ey;
            plastic = (1 - ratio) * delta;
            effective = mu * (ratio + (1 - ratio) * ey / rate);
            derivative = -mu * (1 - ratio) * (ey / rate) / rate;
            potential = 2 * mu * ey * ey + 2 * yieldStress * delta + 2 * ratio * mu * delta * delta;
        }
        double stress = 2 * effective * rate, viscous = rate - plastic;
        if (!double.IsFinite(stress) || !double.IsFinite(potential) || !double.IsFinite(derivative) ||
            !double.IsFinite(viscous) || viscous < 0) throw new ArithmeticException("Constitutive overflow.");
        return new(effective, stress, viscous, plastic, potential, derivative);
    }

    public static PlasticSheetSolution Solve(IReadOnlyList<double> preferredEast,
        IReadOnlyList<double> preferredSouth, IReadOnlyList<double> viscosity,
        IReadOnlyList<double> yieldStress, int side, double dx, double dz, double couplingLength,
        PlasticSheetOptions options, ThinSheetSolution? warmStart = null)
    {
        ArgumentNullException.ThrowIfNull(preferredEast); ArgumentNullException.ThrowIfNull(preferredSouth);
        ArgumentNullException.ThrowIfNull(viscosity); ArgumentNullException.ThrowIfNull(yieldStress);
        ArgumentNullException.ThrowIfNull(options);
        int n = side, count = checked(n * n), size = checked(2 * count);
        if (n < 4 || n > 128 || preferredEast.Count != count || preferredSouth.Count != count ||
            viscosity.Count != count || yieldStress.Count != count ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 ||
            !double.IsFinite(couplingLength) || couplingLength < 0 || couplingLength > n * Math.Min(dx, dz) ||
            preferredEast.Concat(preferredSouth).Any(v => !double.IsFinite(v)) ||
            viscosity.Any(v => !double.IsFinite(v) || v <= 0 || v > 1e6) ||
            yieldStress.Any(v => !double.IsFinite(v) || v <= 0 || v > 1e6))
            throw new ArgumentException("Invalid viscoplastic geometry, fields or budget.");
        if (warmStart is not null && (warmStart.East.Count != count || warmStart.South.Count != count ||
            warmStart.East.Concat(warmStart.South).Any(v => !double.IsFinite(v))))
            throw new ArgumentException("Incompatible warm start.");
        double l2 = couplingLength * couplingLength;
        int[] east = new int[count], west = new int[count], south = new int[count], north = new int[count];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        { int i = z*n+x; east[i]=z*n+(x+1)%n; west[i]=z*n+(x+n-1)%n; south[i]=((z+1)%n)*n+x; north[i]=((z+n-1)%n)*n+x; }
        double[] b = preferredEast.Concat(preferredSouth).ToArray();
        double[] v = warmStart is null ? (double[])b.Clone() : warmStart.East.Concat(warmStart.South).ToArray();
        double norm = Math.Max(1, Math.Sqrt(Dot(b,b))), target = 1e-10 * norm;
        var current = Evaluate(v); double initialEnergy = current.Energy;
        int outer = 0, totalInner = 0;
        double[] dex=new double[count], dez=new double[count], dg=new double[count], dm=new double[count];
        double[] tx=new double[count], tz=new double[count], tg=new double[count];
        while (Math.Sqrt(Dot(current.Gradient,current.Gradient)) > target && outer < 48)
        {
            double[] direction = new double[size], r = current.Gradient.Select(a => -a).ToArray();
            double[] diagonal = new double[size], p = new double[size], zvec = new double[size], ap = new double[size];
            for (int i=0;i<count;i++)
            {
                double corner = Corner(current.Mu,i);
                diagonal[i]=1+l2*(4*(current.Mu[i]+current.Mu[east[i]])/(dx*dx)+(corner+Corner(current.Mu,north[i]))/(dz*dz));
                diagonal[count+i]=1+l2*(4*(current.Mu[i]+current.Mu[south[i]])/(dz*dz)+(corner+Corner(current.Mu,west[i]))/(dx*dx));
            }
            for(int i=0;i<size;i++){zvec[i]=r[i]/diagonal[i];p[i]=zvec[i];}
            double rz=Dot(r,zvec), innerTarget=Math.Max(1e-14,.001*Math.Sqrt(Dot(r,r)));
            int inner=0;
            while(Math.Sqrt(Dot(r,r))>innerTarget && inner<2000)
            {
                Hessian(p,ap,current); double curvature=Dot(p,ap);
                if(!(curvature>0)||!double.IsFinite(curvature))throw new ArithmeticException("Nonpositive viscoplastic tangent.");
                double alpha=rz/curvature;
                for(int i=0;i<size;i++){direction[i]+=alpha*p[i];r[i]-=alpha*ap[i];}
                inner++; if(Math.Sqrt(Dot(r,r))<=innerTarget)break;
                for(int i=0;i<size;i++)zvec[i]=r[i]/diagonal[i];
                double next=Dot(r,zvec), beta=next/rz;
                for(int i=0;i<size;i++)p[i]=zvec[i]+beta*p[i]; rz=next;
            }
            if(inner==2000)throw new ArithmeticException("Viscoplastic tangent solve exceeded its budget.");
            totalInner+=inner;
            double slope=Dot(current.Gradient,direction);
            if(!(slope<0)||!double.IsFinite(slope))throw new ArithmeticException("Newton direction is not a descent direction.");
            bool accepted=false; double step=1;
            for(int backtrack=0;backtrack<24;backtrack++,step*=.5)
            {
                double[] candidate=new double[size];for(int i=0;i<size;i++)candidate[i]=v[i]+step*direction[i];
                var trial=Evaluate(candidate);
                // Near machine precision in energy, a strict force reduction is
                // additionally required; the final force tolerance never changes.
                double rounding=32*2.220446049250313e-16*Math.Max(1,Math.Abs(current.Energy));
                if(trial.Energy<=current.Energy+1e-4*step*slope ||
                    (trial.Energy<=current.Energy+rounding && Dot(trial.Gradient,trial.Gradient)<.25*Dot(current.Gradient,current.Gradient)))
                {v=candidate;current=trial;accepted=true;break;}
            }
            if(!accepted)throw new ArithmeticException("Viscoplastic energy line search failed.");
            outer++;
        }
        current=Evaluate(v);double residual=Math.Sqrt(Dot(current.Gradient,current.Gradient))/norm;
        if(!double.IsFinite(residual)||residual>1e-10)throw new ArithmeticException($"Nonlinear force residual {residual:R} at iteration {outer}.");
        double[] divergence=new double[count];double dissipation=0,work=0,mx=0,mz=0;
        for(int i=0;i<count;i++)
        {
            divergence[i]=current.Ex[i]+current.Ez[i];
            dissipation+=4*l2*current.Mu[i]*current.Rate[i]*current.Rate[i];
            work+=v[i]*(b[i]-v[i])+v[count+i]*(b[count+i]-v[count+i]);
            mx+=v[i]-b[i];mz+=v[count+i]-b[count+i];
        }
        var native=new ThinSheetSolution(Array.AsReadOnly(v[..count]),Array.AsReadOnly(v[count..]),
            Array.AsReadOnly(divergence),Array.AsReadOnly(current.Gamma),totalInner,residual,dissipation,work,Math.Max(Math.Abs(mx),Math.Abs(mz))/count);
        return new(native,Array.AsReadOnly(current.Rate),Array.AsReadOnly(current.Plastic),Array.AsReadOnly(current.Stress),
            Array.AsReadOnly(yieldStress.ToArray()),Array.AsReadOnly(current.Mu),outer,initialEnergy,current.Energy);

        double Corner(double[] a,int i)=>.25*(a[i]+a[east[i]]+a[south[i]]+a[south[east[i]]]);
        void Rates(double[] a,double[] ex,double[] ez,double[] gamma)
        {
            for(int i=0;i<count;i++)
            {ex[i]=(a[i]-a[west[i]])/dx;ez[i]=(a[count+i]-a[count+north[i]])/dz;
             gamma[i]=(a[south[i]]-a[i])/dz+(a[count+east[i]]-a[count+i])/dx;}
        }
        Frame Evaluate(double[] a)
        {
            var f=new Frame(count,size);Rates(a,f.Ex,f.Ez,f.Gamma);
            for(int i=0;i<size;i++)f.Energy+=.5*(a[i]-b[i])*(a[i]-b[i]);
            for(int i=0;i<count;i++)
            {
                double ex=f.Ex[i],ez=f.Ez[i];int w=west[i],no=north[i],nw=north[w];
                double e2=.5*(ex*ex+ez*ez+(ex+ez)*(ex+ez))+
                    (f.Gamma[i]*f.Gamma[i]+f.Gamma[w]*f.Gamma[w]+f.Gamma[no]*f.Gamma[no]+f.Gamma[nw]*f.Gamma[nw])/16;
                f.Rate[i]=Math.Sqrt(e2);var flow=Flow(viscosity[i],f.Rate[i],yieldStress[i],options.Enabled?options.PostYieldRatio:1);
                f.Mu[i]=flow.Viscosity;f.Prime[i]=flow.ViscosityDerivative;f.Plastic[i]=flow.PlasticRate;f.Stress[i]=flow.Stress;
                f.Energy+=l2*flow.Potential;
                f.Tx[i]=2*f.Mu[i]*(2*ex+ez);f.Tz[i]=2*f.Mu[i]*(ex+2*ez);
            }
            for(int i=0;i<count;i++)f.Tg[i]=Corner(f.Mu,i)*f.Gamma[i];
            for(int i=0;i<count;i++)
            {
                f.Gradient[i]=a[i]-b[i]+l2*((f.Tx[i]-f.Tx[east[i]])/dx+(f.Tg[north[i]]-f.Tg[i])/dz);
                f.Gradient[count+i]=a[count+i]-b[count+i]+l2*((f.Tz[i]-f.Tz[south[i]])/dz+(f.Tg[west[i]]-f.Tg[i])/dx);
            }
            if(!double.IsFinite(f.Energy)||f.Gradient.Any(x=>!double.IsFinite(x)))throw new ArithmeticException("Viscoplastic energy overflow.");
            return f;
        }
        void Hessian(double[] a,double[] output,Frame f)
        {
            Rates(a,dex,dez,dg);
            for(int i=0;i<count;i++)
            {
                int w=west[i],no=north[i],nw=north[w];
                double dot=(2*f.Ex[i]+f.Ez[i])*dex[i]+(f.Ex[i]+2*f.Ez[i])*dez[i]+
                    (f.Gamma[i]*dg[i]+f.Gamma[w]*dg[w]+f.Gamma[no]*dg[no]+f.Gamma[nw]*dg[nw])/8;
                dm[i]=f.Rate[i]>0?f.Prime[i]*dot/(2*f.Rate[i]):0;
                tx[i]=2*(f.Mu[i]*(2*dex[i]+dez[i])+dm[i]*(2*f.Ex[i]+f.Ez[i]));
                tz[i]=2*(f.Mu[i]*(dex[i]+2*dez[i])+dm[i]*(f.Ex[i]+2*f.Ez[i]));
            }
            for(int i=0;i<count;i++)tg[i]=Corner(f.Mu,i)*dg[i]+Corner(dm,i)*f.Gamma[i];
            for(int i=0;i<count;i++)
            {
                output[i]=a[i]+l2*((tx[i]-tx[east[i]])/dx+(tg[north[i]]-tg[i])/dz);
                output[count+i]=a[count+i]+l2*((tz[i]-tz[south[i]])/dz+(tg[west[i]]-tg[i])/dx);
            }
        }
    }
    private sealed class Frame
    {
        internal readonly double[] Ex,Ez,Gamma,Rate,Mu,Prime,Plastic,Stress,Tx,Tz,Tg,Gradient;
        internal double Energy;
        internal Frame(int n,int size)
        {Ex=new double[n];Ez=new double[n];Gamma=new double[n];Rate=new double[n];Mu=new double[n];Prime=new double[n];
         Plastic=new double[n];Stress=new double[n];Tx=new double[n];Tz=new double[n];Tg=new double[n];Gradient=new double[size];}
    }
    private static double Dot(double[] a,double[] b)
    {
        double sum=0,c=0;for(int i=0;i<a.Length;i++){double y=a[i]*b[i]-c,t=sum+y;c=(t-sum)-y;sum=t;}return sum;
    }
}
