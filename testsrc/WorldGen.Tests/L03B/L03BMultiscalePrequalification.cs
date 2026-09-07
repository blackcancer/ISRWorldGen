using System.Globalization;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Geology.Landscapes;

namespace ISRWorldGen.Tests.L03B;

/// <summary>
/// Deterministic, in-memory prequalification only.  It deliberately neither reads
/// nor writes Evidence artefacts: a future evidence campaign owns that boundary.
/// Every metric uses the published absolute model altitude, never a display stretch.
/// </summary>
internal static class L03BMultiscalePrequalification
{
    internal static IReadOnlyList<L03BMultiscaleFixture> CreateFamilyFixtures(
        LandscapeModel model,
        AtlasMesh atlas,
        int seed)
    {
        var sites = atlas.Sites.ToDictionary(site => site.Id);
        return model.Cells.GroupBy(cell => cell.Family).OrderBy(group => group.Key).Select(group =>
        {
            LandscapeFamily family = group.Key;
            LandscapeCellProfile cell = group.First();
            AtlasSite site = sites[cell.CellId];
            return new L03BMultiscaleFixture(
                $"F{((int)family) + 1:D2}", family, seed, site.X, site.Z, atlas.Bounds)
            {
                CellId = cell.CellId,
            };
        }).ToArray();
    }

    internal static L03BFixtureAssessment Assess(LandscapeModel model, L03BMultiscaleFixture fixture)
    {
        LandscapeFamilyProfile profile = LandscapeFamilyCatalog.Get(fixture.Family);
        long edgeRadius = Math.Max(1, Math.Min(
            Math.Min(fixture.CenterX - fixture.Bounds.MinX, fixture.Bounds.MaxXExclusive - 1 - fixture.CenterX),
            Math.Min(fixture.CenterZ - fixture.Bounds.MinZ, fixture.Bounds.MaxZExclusive - 1 - fixture.CenterZ)));
        long regionRadius = Math.Max(1, Math.Min(edgeRadius, (long)Math.Round(profile.MacroWavelengthBlocks / 2d)));
        long detailRadius = Math.Max(1, Math.Min(edgeRadius, (long)Math.Round(profile.DetailWavelengthBlocks * 2d)));
        long coreRadius = FindPureCoreRadius(model, fixture, Math.Max(1, Math.Min(detailRadius, (long)Math.Round(profile.DetailWavelengthBlocks / 2d))));

        L03BScaleMeasurement region = Measure(model, fixture, L03BScale.Region, regionRadius, 9);
        L03BScaleMeasurement core = Measure(model, fixture, L03BScale.Core, coreRadius, 5);
        L03BScaleMeasurement detail = Measure(model, fixture, L03BScale.Detail, detailRadius, 9);
        return new L03BFixtureAssessment(fixture, [region, core, detail]);
    }

    internal static IReadOnlyList<L03BBlindPilotLine> CreateBlindPilot(IEnumerable<L03BFixtureAssessment> assessments) =>
        assessments.OrderBy(item => item.Fixture.Code, StringComparer.Ordinal).SelectMany(assessment =>
            assessment.Scales.OrderBy(scale => scale.Scale).Select(scale => new L03BBlindPilotLine(
                assessment.Fixture.Code,
                scale.Scale,
                scale.MinimumAbsoluteAltitude,
                scale.MaximumAbsoluteAltitude,
                scale.AbsoluteAmplitude,
                scale.MaximumAbsoluteProminence,
                scale.ConnectedCoreSamples,
                scale.WorstForeignResidualWeight,
                scale.MaximumTransitionDistanceRatio))).ToArray();

    internal static string RenderBlindPilot(IEnumerable<L03BBlindPilotLine> lines) => string.Join('\n',
        new[] { "fixture,scale,minAbsolute,maxAbsolute,amplitude,prominence,connectedCoreSamples,worstForeignWeight,maxTransitionDistanceRatio" }
        .Concat(lines.OrderBy(line => line.Code, StringComparer.Ordinal).ThenBy(line => line.Scale).Select(line => string.Join(',',
            line.Code,
            line.Scale.ToString(),
            Number(line.MinimumAbsoluteAltitude), Number(line.MaximumAbsoluteAltitude), Number(line.AbsoluteAmplitude),
            Number(line.MaximumAbsoluteProminence), line.ConnectedCoreSamples.ToString(CultureInfo.InvariantCulture),
            Number(line.WorstForeignResidualWeight), Number(line.MaximumTransitionDistanceRatio)))));

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static long FindPureCoreRadius(LandscapeModel model, L03BMultiscaleFixture fixture, long requestedRadius)
    {
        for (long radius = requestedRadius; radius >= 1; radius /= 2)
        {
            if (Grid(fixture.CenterX, fixture.CenterZ, radius, 5).All(point =>
            {
                LandscapeSample sample = model.Sample(point.X, point.Z);
                return sample.DominantCellId == fixture.CellId && !sample.IsTransition &&
                    sample.ForeignResidualWeight == 0d && sample.ForeignResidualContributionNormalized == 0d;
            }))
            {
                return radius;
            }
        }

        throw new InvalidOperationException($"Fixture {fixture.Code} has no connected pure core.");
    }

    private static L03BScaleMeasurement Measure(
        LandscapeModel model,
        L03BMultiscaleFixture fixture,
        L03BScale scale,
        long radius,
        int side)
    {
        LandscapeSample[] samples = Grid(fixture.CenterX, fixture.CenterZ, radius, side)
            .Select(point => model.Sample(point.X, point.Z)).ToArray();
        double[] absolute = samples.Select(sample => sample.ModelAltitudeNormalized).ToArray();
        double mean = absolute.Average();
        return new L03BScaleMeasurement(
            scale,
            radius,
            absolute.Min(),
            absolute.Max(),
            absolute.Max() - absolute.Min(),
            absolute.Max(value => Math.Abs(value - mean)),
            samples.Count(sample => sample.DominantCellId == fixture.CellId && !sample.IsTransition),
            samples.Max(sample => sample.ForeignResidualWeight),
            samples.Max(sample => sample.TransitionDistanceRatio));
    }

    private static IEnumerable<(long X, long Z)> Grid(long centerX, long centerZ, long radius, int side)
    {
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                yield return (
                    centerX + (((2L * radius * x) / (side - 1)) - radius),
                    centerZ + (((2L * radius * z) / (side - 1)) - radius));
            }
        }
    }
}

internal enum L03BScale { Region, Core, Detail }

internal sealed record L03BMultiscaleFixture(string Code, LandscapeFamily Family, int Seed, long CenterX, long CenterZ, WorldBounds Bounds)
{
    internal ISRWorldGen.Core.Foundation.StableId CellId { get; init; }
}

internal sealed record L03BFixtureAssessment(L03BMultiscaleFixture Fixture, IReadOnlyList<L03BScaleMeasurement> Scales);

internal sealed record L03BScaleMeasurement(
    L03BScale Scale,
    long RadiusBlocks,
    double MinimumAbsoluteAltitude,
    double MaximumAbsoluteAltitude,
    double AbsoluteAmplitude,
    double MaximumAbsoluteProminence,
    int ConnectedCoreSamples,
    double WorstForeignResidualWeight,
    double MaximumTransitionDistanceRatio);

/// <summary>Neutral review payload: code and measurements only, never family, seed or site coordinates.</summary>
internal sealed record L03BBlindPilotLine(
    string Code,
    L03BScale Scale,
    double MinimumAbsoluteAltitude,
    double MaximumAbsoluteAltitude,
    double AbsoluteAmplitude,
    double MaximumAbsoluteProminence,
    int ConnectedCoreSamples,
    double WorstForeignResidualWeight,
    double MaximumTransitionDistanceRatio);
