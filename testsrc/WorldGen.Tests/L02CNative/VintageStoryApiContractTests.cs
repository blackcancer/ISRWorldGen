using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen;
using ISRWorldGen.ScaleProfiles;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ISRWorldGen.Tests.L02CNative;

[TestClass]
public sealed class VintageStoryApiContractTests
{
    private const string ExpectedApiSha256 = "034283E7E9D98EAE45EE63005576FD89BADC3C995B531CC4C3FE46F3EB2D3296";

    [TestMethod]
    public void InstalledApiContract_HasAuditedMembersAndLimits()
    {
        Assembly apiAssembly = typeof(ICoreServerAPI).Assembly;
        Assert.AreEqual(new Version(1, 22, 7, 0), apiAssembly.GetName().Version);
        Assert.AreEqual(
            ExpectedApiSha256,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(apiAssembly.Location))));

        AssertProperty<ICoreServerAPI, IServerEventAPI>(nameof(ICoreServerAPI.Event));
        AssertProperty<ICoreServerAPI, IWorldManagerAPI>(nameof(ICoreServerAPI.WorldManager));
        AssertProperty<ICoreServerAPI, IServerAPI>(nameof(ICoreServerAPI.Server));
        AssertProperty<IWorldManagerAPI, ISaveGame>(nameof(IWorldManagerAPI.SaveGame));
        AssertProperty<IWorldManagerAPI, int>(nameof(IWorldManagerAPI.MapSizeX));
        AssertProperty<IWorldManagerAPI, int>(nameof(IWorldManagerAPI.MapSizeY));
        AssertProperty<IWorldManagerAPI, int>(nameof(IWorldManagerAPI.MapSizeZ));
        AssertProperty<IWorldManagerAPI, int>(nameof(IWorldManagerAPI.ChunkSize));
        AssertProperty<ISaveGame, bool>(nameof(ISaveGame.IsNew));
        AssertProperty<ISaveGame, string>(nameof(ISaveGame.SavegameIdentifier));
        AssertProperty<ISaveGame, ITreeAttribute>(nameof(ISaveGame.WorldConfiguration));
        AssertProperty<IWorldAccessor, ITreeAttribute>(nameof(IWorldAccessor.Config));

        AssertMethod<IServerEventAPI>(
            nameof(IServerEventAPI.ServerRunPhase),
            typeof(void),
            typeof(EnumServerRunPhase),
            typeof(Action));
        AssertMethod<IServerEventAPI>(
            nameof(IServerEventAPI.InitWorldGenerator),
            typeof(void),
            typeof(Action),
            typeof(string));
        AssertMethod<ISaveGame>(nameof(ISaveGame.GetData), typeof(byte[]), typeof(string));
        AssertMethod<ISaveGame>(nameof(ISaveGame.StoreData), typeof(void), typeof(string), typeof(byte[]));
        AssertMethod<IServerAPI>(nameof(IServerAPI.ShutDown), typeof(void));

        AssertConstant(nameof(GlobalConstants.ChunkSize), 32);
        AssertConstant(nameof(GlobalConstants.MaxWorldSizeXZ), 67_108_864);
        AssertConstant(nameof(GlobalConstants.MaxWorldSizeY), 16_384);
    }

    [TestMethod]
    public void ModPackage_ContainsCoreDependencyWithoutRuntimeOrImplicitL00Selection()
    {
        string repositoryRoot = GetRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string packageRoot = Path.Combine(
            repositoryRoot,
            "src",
            "WorldGen.VintageStory",
            "bin",
            configuration,
            "Mods",
            "isrworldgen");
        string modAssemblyPath = Path.Combine(packageRoot, "ISRWorldGen.dll");
        string coreAssemblyPath = Path.Combine(packageRoot, "ISRWorldGen.Core.dll");
        Assert.IsTrue(File.Exists(modAssemblyPath), $"Missing packaged mod assembly: {modAssemblyPath}");
        Assert.IsTrue(File.Exists(coreAssemblyPath), $"Missing packaged Core dependency: {coreAssemblyPath}");
        Assert.IsFalse(File.Exists(Path.Combine(packageRoot, "ISRWorldGen.Runtime.dll")));

        AssemblyName[] references = typeof(ISRWorldGenModSystem).Assembly.GetReferencedAssemblies();
        Assert.IsTrue(references.Any(reference => reference.Name == "ISRWorldGen.Core"));

        string launchSettings = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "WorldGen.VintageStory",
            "Properties",
            "launchSettings.json"));
        Assert.IsFalse(
            launchSettings.Contains(VintageStoryNativeProfileHost.SelectionConfigKey, StringComparison.Ordinal),
            "Existing L00 launch profiles must not opt into a scale profile implicitly.");

        string packagedWorldConfigPath = Path.Combine(packageRoot, "worldconfig.json");
        Assert.IsTrue(
            File.Exists(packagedWorldConfigPath),
            $"Missing packaged native world configuration declaration: {packagedWorldConfigPath}");
        AssertWorldConfigDeclaration(packagedWorldConfigPath);
    }

    [TestMethod]
    public void WorldConfigDeclaration_RegistersBoundedExplicitProfileSelection()
    {
        string sourcePath = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "WorldGen.VintageStory",
            "worldconfig.json");

        AssertWorldConfigDeclaration(sourcePath);

        ModWorldConfiguration? declaration =
            JsonConvert.DeserializeObject<ModWorldConfiguration>(File.ReadAllText(sourcePath));
        Assert.IsNotNull(declaration);
        Assert.HasCount(1, declaration.WorldConfigAttributes);
        WorldConfigurationAttribute attribute = declaration.WorldConfigAttributes[0];
        Assert.AreEqual(VintageStoryNativeProfileHost.SelectionConfigKey, attribute.Code);
        Assert.AreEqual(EnumDataType.String, attribute.DataType);
        Assert.AreEqual(string.Empty, attribute.TypedDefault);
        Assert.IsFalse(attribute.OnCustomizeScreen);
        Assert.IsTrue(attribute.OnlyDuringWorldCreate);
    }

    [TestMethod]
    public void SelectionConfig_MissingEmptyOrWhitespace_RemainsInactive()
    {
        foreach (string? value in new string?[] { null, string.Empty, " \t" })
        {
            var config = new TreeAttribute();
            if (value is not null)
            {
                config.SetString(VintageStoryNativeProfileHost.SelectionConfigKey, value);
            }

            NativeProfileSelection selection = VintageStoryNativeProfileHost.ReadSelection(config);

            Assert.IsFalse(selection.IsSpecified, $"Value '{value}' must not opt into ISRWorldGen.");
            Assert.IsNull(selection.ProfileId);
        }
    }

    [TestMethod]
    public void SelectionConfig_LaboratoryValue_IsAvailableBeforeGameReadyBridge()
    {
        var config = new TreeAttribute();
        config.SetString(VintageStoryNativeProfileHost.SelectionConfigKey, "laboratory");

        NativeProfileSelection selection = VintageStoryNativeProfileHost.ReadSelection(config);

        Assert.IsTrue(selection.IsSpecified);
        Assert.AreEqual("laboratory", selection.ProfileId);
    }

    private static void AssertProperty<TDeclaring, TProperty>(string name)
    {
        PropertyInfo? property = typeof(TDeclaring).GetProperty(name);
        Assert.IsNotNull(property, $"Missing {typeof(TDeclaring).FullName}.{name}.");
        Assert.AreEqual(typeof(TProperty), property.PropertyType);
    }

    private static void AssertMethod<TDeclaring>(string name, Type returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = typeof(TDeclaring).GetMethod(name, parameterTypes);
        Assert.IsNotNull(method, $"Missing {typeof(TDeclaring).FullName}.{name}.");
        Assert.AreEqual(returnType, method.ReturnType);
    }

    private static void AssertConstant(string name, int expected)
    {
        FieldInfo? field = typeof(GlobalConstants).GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(field, $"Missing {typeof(GlobalConstants).FullName}.{name}.");
        Assert.AreEqual(expected, field.GetRawConstantValue());
    }

    private static void AssertWorldConfigDeclaration(string path)
    {
        Assert.IsTrue(File.Exists(path), $"Missing world configuration declaration: {path}");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement attributes = document.RootElement.GetProperty("worldConfigAttributes");
        Assert.AreEqual(JsonValueKind.Array, attributes.ValueKind);
        Assert.AreEqual(1, attributes.GetArrayLength());

        JsonElement attribute = attributes[0];
        Assert.AreEqual(VintageStoryNativeProfileHost.SelectionConfigKey, attribute.GetProperty("code").GetString());
        Assert.AreEqual("String", attribute.GetProperty("dataType").GetString());
        Assert.AreEqual(string.Empty, attribute.GetProperty("default").GetString());
        Assert.IsFalse(attribute.GetProperty("onCustomizeScreen").GetBoolean());
        Assert.IsTrue(attribute.GetProperty("onlyDuringWorldCreate").GetBoolean());
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "..", ".."));
}
