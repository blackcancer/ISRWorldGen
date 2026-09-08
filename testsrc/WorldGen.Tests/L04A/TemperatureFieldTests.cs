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
}
