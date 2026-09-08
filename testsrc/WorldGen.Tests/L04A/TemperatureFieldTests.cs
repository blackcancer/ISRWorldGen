using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L04A;

[TestClass]
public sealed class TemperatureFieldTests
{
    private static readonly LatitudeAxis NorthSouth = new(new WorldBlockPosition(0, 0), 0d, 1d, 100d);
    private static readonly TemperatureSettings Settings = new(30d, 0.4d, 0.006d, -4d);

    [TestMethod]
    public void T04_01_GradientUsesLatitudeAltitudeAndContinentalityWithOneExplicitAltitudeCorrection()
    {
        TemperatureSample equatorialCoast = TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), 0d, 0d));
        TemperatureSample polewardCoast = TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 5_000), 0d, 0d));
        TemperatureSample mountainCoast = TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), 1_000d, 0d));
        TemperatureSample equatorialInland = TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), 0d, 1d));

        Assert.AreEqual(30d, equatorialCoast.ReferenceCelsius, 1e-12);
        Assert.AreEqual(10d, polewardCoast.ReferenceCelsius, 1e-12);
        Assert.AreEqual(24d, mountainCoast.SurfaceCelsius, 1e-12);
        Assert.AreEqual(6d, mountainCoast.AltitudeCorrectionCelsius, 1e-12);
        Assert.AreEqual(26d, equatorialInland.ReferenceCelsius, 1e-12);

        NativeTemperatureExport engineCorrected = TemperatureField.ExportToNative(mountainCoast,
            NativeAltitudeCorrection.EngineAppliesAltitude);
        NativeTemperatureExport coreCorrected = TemperatureField.ExportToNative(mountainCoast,
            NativeAltitudeCorrection.CoreAppliesAltitude);
        Assert.AreEqual(30d, engineCorrected.Celsius, 1e-12, "Native receives the reference exactly once when it owns altitude.");
        Assert.AreEqual(24d, coreCorrected.Celsius, 1e-12, "Native receives the corrected surface when Core owns altitude.");
    }

    [TestMethod]
    public void T04_01_InvalidInputAndOutOfRangeLatitudeFailRatherThanClamp()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), double.NaN, 0d)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), 0d, 1.1d)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 9_001), 0d, 0d)));
    }

    [TestMethod]
    public void T04_02_NorthSouthAxisIsAcceptedAndObliqueAxisIsExplicitlyRejectedForNativeSeasons()
    {
        TemperatureField.EnsureNativeSeasonSupport(NorthSouth);
        var oblique = new LatitudeAxis(new WorldBlockPosition(0, 0), 1d, 1d, 100d);

        Assert.AreEqual(7.0710678118654755d, oblique.GetLatitudeDegrees(new WorldBlockPosition(1_000, 0)), 1e-12);
        NotSupportedException exception = Assert.ThrowsExactly<NotSupportedException>(
            () => TemperatureField.EnsureNativeSeasonSupport(oblique));
        StringAssert.Contains(exception.Message, "North-South");
    }

    [TestMethod]
    public void LatitudeAxis_NormalizesExtremeFiniteAxesAndKeepsEquatorialSymmetries()
    {
        var tinyDiagonal = new LatitudeAxis(new WorldBlockPosition(0, 0), double.Epsilon, double.Epsilon, 1d);
        var hugeDiagonal = new LatitudeAxis(new WorldBlockPosition(0, 0), double.MaxValue, double.MaxValue, 1d);
        var northSouthReversed = new LatitudeAxis(new WorldBlockPosition(0, 0), 0d, -1d, 100d);

        Assert.AreEqual(1d, Math.Sqrt((tinyDiagonal.AxisX * tinyDiagonal.AxisX) + (tinyDiagonal.AxisZ * tinyDiagonal.AxisZ)), 1e-12);
        Assert.AreEqual(1d, Math.Sqrt((hugeDiagonal.AxisX * hugeDiagonal.AxisX) + (hugeDiagonal.AxisZ * hugeDiagonal.AxisZ)), 1e-12);
        Assert.AreEqual(10d, NorthSouth.GetLatitudeDegrees(new WorldBlockPosition(0, 1_000)), 1e-12);
        Assert.AreEqual(-10d, NorthSouth.GetLatitudeDegrees(new WorldBlockPosition(0, -1_000)), 1e-12);
        Assert.AreEqual(-10d, northSouthReversed.GetLatitudeDegrees(new WorldBlockPosition(0, 1_000)), 1e-12);
        TemperatureField.EnsureNativeSeasonSupport(northSouthReversed);

        TemperatureSample north = TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 1_000), 0d, 0.5d));
        TemperatureSample south = TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, -1_000), 0d, 0.5d));
        Assert.AreEqual(north.ReferenceCelsius, south.ReferenceCelsius, 1e-12);
        Assert.AreEqual(north.SurfaceCelsius, south.SurfaceCelsius, 1e-12);
    }

    [TestMethod]
    public void TemperatureField_PreservesContinentalityAndCoordinateBoundariesWithoutSilentClamping()
    {
        TemperatureSample coast = TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), -500d, 0d));
        TemperatureSample inland = TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), -500d, 1d));

        Assert.AreEqual(Settings.InlandReferenceOffsetCelsius, inland.ReferenceCelsius - coast.ReferenceCelsius, 1e-12);
        Assert.AreEqual(coast.ReferenceCelsius + 3d, coast.SurfaceCelsius, 1e-12,
            "Negative model altitude remains explicit rather than being silently clamped to sea level.");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NorthSouth.GetLatitudeDegrees(
            new WorldBlockPosition(long.MaxValue, long.MinValue)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), double.PositiveInfinity, 0d)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemperatureField.Calculate(NorthSouth, Settings,
            new TemperatureInput(new WorldBlockPosition(0, 0), 0d, double.NegativeInfinity)));
    }

    [TestMethod]
    public void TemperatureField_IsDeterministicAcrossInputPermutationsAndRejectsInvalidConfigurations()
    {
        TemperatureInput[] inputs =
        [
            new(new WorldBlockPosition(0, 0), 0d, 0d),
            new(new WorldBlockPosition(100, 200), 250d, 0.25d),
            new(new WorldBlockPosition(-100, -200), -250d, 1d)
        ];
        TemperatureSample[] forward = inputs.Select(input => TemperatureField.Calculate(NorthSouth, Settings, input)).ToArray();
        TemperatureSample[] reverse = inputs.Reverse().Select(input => TemperatureField.Calculate(NorthSouth, Settings, input)).Reverse().ToArray();

        CollectionAssert.AreEqual(forward, reverse);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LatitudeAxis(new WorldBlockPosition(0, 0), 0d, 0d, 100d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LatitudeAxis(new WorldBlockPosition(0, 0), 1d, 0d, 0d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TemperatureSettings(0d, -0.1d, 0d, 0d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TemperatureSettings(0d, 0d, double.NaN, 0d));
    }

    [TestMethod]
    public void NativeExport_StrictlySelectsReferenceOrSurfaceAndRejectsInvalidModesAndSamples()
    {
        var sample = new TemperatureSample(15d, 12d, 8d, 4d);

        Assert.AreEqual(12d, TemperatureField.ExportToNative(sample,
            NativeAltitudeCorrection.EngineAppliesAltitude).Celsius, 1e-12);
        Assert.AreEqual(8d, TemperatureField.ExportToNative(sample,
            NativeAltitudeCorrection.CoreAppliesAltitude).Celsius, 1e-12);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemperatureField.ExportToNative(sample,
            (NativeAltitudeCorrection)42));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemperatureField.ExportToNative(
            new TemperatureSample(0d, 1d, 1d, double.NaN), NativeAltitudeCorrection.CoreAppliesAltitude));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemperatureField.ExportToNative(
            new TemperatureSample(0d, 1d, 0d, 0d), NativeAltitudeCorrection.CoreAppliesAltitude));
    }

    [TestMethod]
    public void TemperatureEvaluation_IsImmutableSelfDescribingAndStableAcrossRepeatedCalls()
    {
        TemperatureInput input = new(new WorldBlockPosition(320, -640), 125d, 0.75d);
        TemperatureEvaluation first = TemperatureField.Evaluate(NorthSouth, Settings, input);
        TemperatureEvaluation second = TemperatureField.Evaluate(NorthSouth, Settings, input);

        Assert.AreEqual(TemperatureField.AlgorithmVersion, first.AlgorithmVersion);
        Assert.AreSame(NorthSouth, first.LatitudeAxis);
        Assert.AreSame(Settings, first.Settings);
        Assert.AreEqual(input, first.Input);
        Assert.AreEqual(first.Sample, second.Sample);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.Sample.ReferenceCelsius, TemperatureField.ExportToNative(first.Sample,
            NativeAltitudeCorrection.EngineAppliesAltitude).Celsius, 1e-12);
        Assert.AreEqual(first.Sample.SurfaceCelsius, TemperatureField.ExportToNative(first.Sample,
            NativeAltitudeCorrection.CoreAppliesAltitude).Celsius, 1e-12);
    }
}
