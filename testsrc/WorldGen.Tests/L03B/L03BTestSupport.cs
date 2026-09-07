using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03B;

internal static class L03BTestSupport
{
    private const string AssetHash = "cfd370ef8c6540792e17507897d0db5a9d96b621a805d1ccc605abcf89460ddf";

    internal static FrozenScaleProfile FrozenProfile(string id)
    {
        ScaleProfileDefinition definition = ScaleProfileCatalog.Proposals.Single(item => item.Id == id);
        var constraints = new NativeWorldConstraints(
            "l03b-qualified-native-v1",
            1,
            [256, 384, 512],
            4_096,
            1_024_000,
            512);
        return Success(ScaleProfileValidator.ValidateAndFreeze(definition, constraints));
    }

    internal static GenerationIdentity Identity(int seed, FrozenScaleProfile profile) => new(
        seed,
        GenerationIdentity.SupportedAlgorithmVersion,
        GenerationIdentity.SupportedSchemaVersion,
        profile.GeographyConfigHash,
        Hash256.Parse(AssetHash),
        $"l03b-{profile.Id}-v{profile.ProfileVersion}");

    internal static (AtlasMesh Atlas, PlateAtlasSnapshot Plates) PlateFixture(
        int seed,
        FrozenScaleProfile profile)
    {
        GenerationIdentity identity = Identity(seed, profile);
        WorldDomain domain = profile.AtlasIndexProfile.Domain;
        var bounds = new WorldBounds(
            domain.X.MinInclusive,
            domain.Z.MinInclusive,
            domain.X.MaxExclusive,
            domain.Z.MaxExclusive);
        GeneratedSiteSet sites = Success(AtlasSiteGenerator.Generate(
            identity,
            bounds,
            new AtlasSiteGenerationSettings(profile.RequestedSiteCount)));
        AtlasMesh atlas = Success(AtlasGeometryBuilder.Build(
            identity,
            bounds,
            sites.Sites,
            new AtlasGeometryBuildOptions(1, GeometryCacheMode.Cold)));
        var continentSettings = new ContinentalFieldSettings(5, 18, 64, 1_000_000);
        PlateAtlasSnapshot plates = Success(PlateAtlasBuilder.Build(
            identity,
            atlas,
            profile,
            new PlateGenerationSettings(
                Math.Min(7, atlas.Sites.Count),
                continentSettings,
                profile.SiteQuota,
                4_000,
                4_000_000)));
        return (atlas, plates);
    }

    internal static T Success<T>(GenerationResult<T> result)
        where T : class
    {
        Assert.IsInstanceOfType<GenerationSuccess<T>>(result);
        return ((GenerationSuccess<T>)result).Snapshot;
    }

    internal static IReadOnlyList<int> SeedCorpus(string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(FindRepositoryRoot(), "registry", "fixtures.json")));
        return document.RootElement.GetProperty(propertyName)
            .EnumerateArray()
            .Select(item => item.GetInt32())
            .ToArray();
    }

    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ISRWorldGen.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    internal static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
