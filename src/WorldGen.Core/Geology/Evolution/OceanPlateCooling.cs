using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Constant-property finite-plate conduction. This is a declared thermal prior,
/// not the temperature/pressure-dependent Earth inversion of Richards et al. 2018.
/// The cold-fraction spectrum has positive weights; its unresolved odd-mode tail
/// is lumped at the slowest omitted mode. The resulting error is explicitly bounded.
/// </summary>
public sealed record OceanCoolingOptions
{
    public const string AlgorithmId = "ocean-finite-plate-carried-thermal-spectrum-v1";
    public int RetainedOddModes { get; }
    public double PlateThicknessKm { get; }
    public double DiffusivityM2PerSecond { get; }
    public double ExpansionPerKelvin { get; }
    public double TemperatureContrastKelvin { get; }
    public double AnchorAgeMyr { get; }
    public double FundamentalTimeMyr => Math.Pow(1000 * PlateThicknessKm, 2) /
        (Math.PI * Math.PI * DiffusivityM2PerSecond * 31_557_600_000_000d);
    public double DryContractionKm => ExpansionPerKelvin * TemperatureContrastKelvin * PlateThicknessKm / 2;
    public double TailWeight => 1 - Enumerable.Range(0, RetainedOddModes).Sum(k => 8 / (Math.PI * Math.PI * Math.Pow(2 * k + 1, 2)));
    // For a single response anchored to the exact finite-plate law: two evaluations
    // each have error <= tail weight. This is NOT the PNG quantization error.
    public double ConservativeElevationErrorKm => 2 * DryContractionKm * TailWeight / (1 - 1030d / 3300d);

    public OceanCoolingOptions(int retainedOddModes = 12, double plateThicknessKm = 125,
        double diffusivityM2PerSecond = 1e-6, double expansionPerKelvin = 3.2e-5,
        double temperatureContrastKelvin = 1350, double anchorAgeMyr = 50)
    {
        if (retainedOddModes is < 4 or > 32 || !double.IsFinite(plateThicknessKm) || plateThicknessKm is < 50 or > 200 ||
            !double.IsFinite(diffusivityM2PerSecond) || diffusivityM2PerSecond is < 1e-7 or > 5e-6 ||
            !double.IsFinite(expansionPerKelvin) || expansionPerKelvin is < 1e-5 or > 5e-5 ||
            !double.IsFinite(temperatureContrastKelvin) || temperatureContrastKelvin is < 800 or > 1800 ||
            !double.IsFinite(anchorAgeMyr) || anchorAgeMyr is < 0 or > 200)
            throw new ArgumentException("Invalid finite-plate thermal parameters or spectrum budget.");
        RetainedOddModes = retainedOddModes; PlateThicknessKm = plateThicknessKm;
        DiffusivityM2PerSecond = diffusivityM2PerSecond; ExpansionPerKelvin = expansionPerKelvin;
        TemperatureContrastKelvin = temperatureContrastKelvin; AnchorAgeMyr = anchorAgeMyr;
    }
    internal double Weight(int mode) => mode == RetainedOddModes ? TailWeight : 8 / (Math.PI * Math.PI * Math.Pow(2 * mode + 1, 2));
    internal double Decay(int mode, double age) => Math.Exp(-Math.Pow(2 * mode + 1, 2) * age / FundamentalTimeMyr);
    public double ColdFraction(double ageMyr)
    {
        if (!double.IsFinite(ageMyr) || ageMyr < 0) throw new ArgumentOutOfRangeException(nameof(ageMyr));
        double sum = 0;
        for (int k = 0; k <= RetainedOddModes; k++) sum += Weight(k) * (1 - Decay(k, ageMyr));
        return sum;
    }
    public double ElevationKm(double continental, double oceanic, double coldFraction)
    {
        if (!double.IsFinite(continental) || continental < 0 || !double.IsFinite(oceanic) || oceanic < 0 ||
            !double.IsFinite(continental + oceanic) || continental + oceanic <= 0 ||
            !double.IsFinite(coldFraction) || coldFraction < -1e-13 || coldFraction > 1 + 1e-13)
            throw new ArgumentException("Invalid thermal material column.");
        // Keep the old column datum at ONE declared reference age for a controlled
        // A/B experiment; never normalize individual maps or move the sea level.
        double fraction = oceanic / (continental + oceanic);
        double dry = continental * (3300d - 2800d) / 3300d + oceanic * (3300d - 2900d) / 3300d - 4.95
            + fraction * (1.6 - .25 * Math.Sqrt(AnchorAgeMyr)
                - DryContractionKm * (coldFraction - ColdFraction(AnchorAgeMyr)));
        return dry >= 0 ? dry : dry / (1 - 1030d / 3300d);
    }
}

public sealed record OceanThermalReceipt(double Time, double MaximumBalanceResidual,
    double OceanVolumeSum, double ColdVolumeSum);

/// <summary>
/// Extensive ocean-carried thermal modes, one spectrum per MATERIAL origin.
/// Equal mean ages need not mean equal temperatures. Newly created basalt has
/// zero cold deficit. Recycling removes only the selected origin's own spectrum.
/// This is one-way thermal/isostatic coupling: rheology/polarity still use age.
/// It neither invents an inherited age geography nor filters the height field.
/// </summary>
public sealed class OceanThermalCohorts
{
    public OceanCoolingOptions Options { get; }
    public int Side { get; }
    public int PlateCount => ocean.Length;
    private readonly double[][] ocean;
    private readonly double[][][] modes; // origin / odd mode + tail / extensive warm amplitude
    public double MaximumBalanceResidual { get; }
    private OceanThermalCohorts(int side, OceanCoolingOptions options, double[][] ocean,
        double[][][] modes, double residual)
    { Side = side; Options = options; this.ocean = ocean; this.modes = modes; MaximumBalanceResidual = residual; }

    public static OceanThermalCohorts Create(MaterialPlateCohorts state, OceanCoolingOptions options)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(options);
        int count = state.Side * state.Side;
        var volume = new double[state.PlateCount][]; var spectrum = new double[state.PlateCount][][];
        for (int p = 0; p < state.PlateCount; p++)
        {
            volume[p] = new double[count]; spectrum[p] = new double[options.RetainedOddModes + 1][];
            for (int k = 0; k < spectrum[p].Length; k++) spectrum[p][k] = new double[count];
            for (int i = 0; i < count; i++)
            {
                double o = state.Value(p, 1, i), age = o > 0 ? state.Value(p, 2, i) / o : 0;
                volume[p][i] = o;
                for (int k = 0; k < spectrum[p].Length; k++) spectrum[p][k][i] = o * options.Decay(k, age);
            }
        }
        return new(state.Side, options, volume, spectrum, 0);
    }

    public ReadOnlyCollection<double> ColdFractions()
    {
        int count = Side * Side; var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            double total = 0, cold = 0;
            for (int p = 0; p < PlateCount; p++)
            {
                double o = ocean[p][i]; total += o;
                for (int k = 0; k <= Options.RetainedOddModes; k++) cold += Options.Weight(k) * (o - modes[p][k][i]);
            }
            result[i] = total > 0 ? cold / total : 0;
            if (!double.IsFinite(result[i]) || result[i] < -1e-13 || result[i] > 1 + 1e-13)
                throw new ArithmeticException("Thermal deficit lost its oceanic carrier.");
        }
        return Array.AsReadOnly(result);
    }

    public OceanThermalCohorts Advance(MaterialPlateCohorts before, MaterialPlateCohorts movedAndAged,
        double[] east, double[] south, double dx, double dz, double dt,
        double[] born, int[] lower, double[] removed)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(movedAndAged);
        ArgumentNullException.ThrowIfNull(east); ArgumentNullException.ThrowIfNull(south);
        ArgumentNullException.ThrowIfNull(born); ArgumentNullException.ThrowIfNull(lower); ArgumentNullException.ThrowIfNull(removed);
        int n = Side, count = n * n;
        if (before.Side != n || movedAndAged.Side != n || before.PlateCount != PlateCount || movedAndAged.PlateCount != PlateCount ||
            east.Length != count || south.Length != count || born.Length != count || lower.Length != count || removed.Length != count ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 || !double.IsFinite(dt) || dt < 0)
            throw new ArgumentException("Invalid ocean thermal transition geometry.");
        var donor = new int[count * 5]; var coefficient = new double[count * 5];
        double ax = dt / dx, az = dt / dz;
        var mass = new double[count];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x, e = z * n + (x + 1) % n, w = z * n + (x + n - 1) % n;
            int s = ((z + 1) % n) * n + x, no = ((z + n - 1) % n) * n + x;
            if (!double.IsFinite(east[i]) || !double.IsFinite(south[i])) throw new ArgumentException("Nonfinite face velocity.");
            double outgoing = ax * (Math.Max(east[i], 0) + Math.Max(-east[w], 0)) + az * (Math.Max(south[i], 0) + Math.Max(-south[no], 0));
            if (!double.IsFinite(outgoing) || outgoing > 1) throw new ArgumentException("Thermal carrier CFL exceeded.");
            donor[5*i] = i; donor[5*i+1] = e; donor[5*i+2] = w; donor[5*i+3] = s; donor[5*i+4] = no;
            coefficient[5*i] = 1-outgoing; coefficient[5*i+1] = ax*Math.Max(-east[i],0); coefficient[5*i+2] = ax*Math.Max(east[w],0);
            coefficient[5*i+3] = az*Math.Max(-south[i],0); coefficient[5*i+4] = az*Math.Max(south[no],0);
            for (int p = 0; p < PlateCount; p++) mass[i] += movedAndAged.Value(p,0,i) + movedAndAged.Value(p,1,i);
            if (!double.IsFinite(born[i]) || born[i] < 0 || !double.IsFinite(removed[i]) || removed[i] < 0 ||
                lower[i] < -1 || lower[i] >= PlateCount || (born[i] > 0 && removed[i] > 0) ||
                (born[i] > 0 && !(mass[i] > 0)) || (removed[i] > 0 && (lower[i] < 0 || removed[i] > movedAndAged.Value(lower[i],1,i))))
                throw new ArgumentException("Invalid thermal source or selective sink.");
        }
        var nextOcean = new double[PlateCount][]; var nextModes = new double[PlateCount][][];
        double maximumResidual = MaximumBalanceResidual;
        for (int p = 0; p < PlateCount; p++)
        {
            double[] packet = new double[5*count], birth = new double[count], surviving = new double[count];
            for (int i = 0; i < count; i++)
                if (ocean[p][i] != before.Value(p,1,i)) throw new ArgumentException("Thermal state belongs to a different material step.");
            nextOcean[p] = new double[count]; nextModes[p] = new double[Options.RetainedOddModes+1][];
            for (int i = 0; i < count; i++)
            {
                double carried = 0;
                for (int a = 0; a < 5; a++) { int j=5*i+a; packet[j]=ocean[p][donor[j]]*coefficient[j]; carried+=packet[j]; }
                double o = movedAndAged.Value(p,1,i);
                if (carried != o) throw new ArithmeticException("Thermal and material donor packets disagree.");
                birth[i] = born[i] > 0 ? born[i] * ((movedAndAged.Value(p,0,i)+o)/mass[i]) : 0;
                surviving[i] = o - (lower[i] == p ? removed[i] : 0);
                nextOcean[p][i] = surviving[i]+birth[i];
            }
            for (int k = 0; k <= Options.RetainedOddModes; k++)
            {
                var concentration = new double[count];
                for (int i=0;i<count;i++)
                {
                    concentration[i] = ocean[p][i] > 0 ? modes[p][k][i]/ocean[p][i] : 0;
                    if (!double.IsFinite(concentration[i]) || concentration[i] < 0 || concentration[i] > 1)
                        throw new ArithmeticException("Invalid carried thermal concentration.");
                }
                double[] output = new double[count], carriedModes = new double[count], lost = new double[count];
                double decay = Options.Decay(k,dt);
                for (int i=0;i<count;i++)
                {
                    double carried = 0;
                    for (int a=0;a<5;a++) { int j=5*i+a; carried += packet[j]*concentration[donor[j]]; }
                    carried *= decay; carriedModes[i] = carried;
                    double movedO = movedAndAged.Value(p,1,i);
                    double kept = movedO > 0 ? surviving[i]*(carried/movedO) : 0;
                    output[i] = kept + birth[i]; lost[i] = carried-kept;
                    if (!double.IsFinite(output[i]) || output[i]<0 || output[i]>nextOcean[p][i])
                        throw new ArithmeticException("Thermal moment is outside its surviving carrier.");
                }
                double old = CrustTransport.Sum(modes[p][k])*decay;
                double transferred = CrustTransport.Sum(carriedModes);
                CrustTransport.RequireBalance(old, transferred, "thermal mode transport/decay");
                double expected = transferred-CrustTransport.Sum(lost)+CrustTransport.Sum(birth);
                double actual = CrustTransport.Sum(output);
                CrustTransport.RequireBalance(expected,actual,"thermal mode sources/recycling");
                maximumResidual = Math.Max(maximumResidual,Math.Max(Math.Abs(old-transferred),Math.Abs(expected-actual))/Math.Max(1,Math.Abs(old)));
                nextModes[p][k]=output;
            }
        }
        return new(n,Options,nextOcean,nextModes,maximumResidual);
    }
    public void RequireCarriers(MaterialPlateCohorts state)
    {
        if (state.Side != Side || state.PlateCount != PlateCount) throw new ArgumentException("Different thermal carrier grid.");
        for(int p=0;p<PlateCount;p++) for(int i=0;i<Side*Side;i++)
            if(ocean[p][i]!=state.Value(p,1,i)) throw new ArithmeticException("Thermal state does not follow the material source/sink.");
    }
}
