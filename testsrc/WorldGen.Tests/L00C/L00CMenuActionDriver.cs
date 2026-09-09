// Debug-only bridge for the exact Vintage Story 1.22.7 native single-player
// actions used by the immutable L00-C S2 scenario. It never uses a menu cell,
// list index or graphical readiness state.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace ISRWorldGen.L00C.Laboratory;

internal enum L00CManagerResolutionStatus
{
    ApiTypeMismatch,
    GameUnavailable,
    RunningScreenUnavailable,
    ManagerUnavailable,
    Ready
}

internal sealed class L00CManagerResolution
{
    internal L00CManagerResolution(L00CManagerResolutionStatus status, object? clientMain, object? screenManager)
    { Status = status; ClientMain = clientMain; ScreenManager = screenManager; }
    internal L00CManagerResolutionStatus Status { get; }
    internal object? ClientMain { get; }
    internal object? ScreenManager { get; }
}

internal sealed class L00CMenuActionReceipt
{
    internal L00CMenuActionReceipt(string action, DateTimeOffset startedUtc, DateTimeOffset completedUtc,
        string vintagestoryLibVersion, string vintagestoryLibSha256, string targetMethod, bool completed)
    {
        Action = action;
        StartedUtc = startedUtc;
        CompletedUtc = completedUtc;
        VintagestoryLibVersion = vintagestoryLibVersion;
        VintagestoryLibSha256 = vintagestoryLibSha256;
        TargetMethod = targetMethod;
        Completed = completed;
    }

    internal string Action { get; }
    internal DateTimeOffset StartedUtc { get; }
    internal DateTimeOffset CompletedUtc { get; }
    internal string VintagestoryLibVersion { get; }
    internal string VintagestoryLibSha256 { get; }
    internal string TargetMethod { get; }
    internal bool Completed { get; }
}

/// <summary>
/// Version/hash locked native adapter. Session objects exist only in method
/// locals and never cross a Save &amp; Quit boundary.
/// </summary>
internal static class L00CMenuActionDriver
{
    internal const string RequiredLibVersion = "1.22.7.0";
    internal const string RequiredLibSha256 = "E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0";
    internal const int SaveQuitLeaveReason = 0;

    private const string ConnectToSingleplayerIlSha256 = "51294EE9CECF73EAB1583CADF61A8C748EEC33500EFAD01FA803060B0D971488";
    private const string StartMainMenuIlSha256 = "1E05347AF060DEAAE53BD35C8C45FFA0639CC1CFF626125C23ADEF111F06BBD2";
    private const string DestroyGameSessionIlSha256 = "7D381CA230F83A2D845FFFE03E571447D1C4CAD9F26752F2E2E9AB74CC4EB4CE";
    private const string ServerThreadStartIlSha256 = "B419BC6870F6FBFA2F47164D8988087846A266F467E5DC215835A137C602AE05";
    private const string ServerStopIlSha256 = "135A7F3587A092A8ED5792D37490CDDE32B96B65029072FEBF3A31AD053C91BE";

    internal static L00CMenuActionReceipt EnsureMainMenu(object screenManager)
    {
        RequireDebugLab();
        MethodInfo startMainMenu = RequireScreenManagerMethod(screenManager, "StartMainMenu", 0x2133, Type.EmptyTypes, StartMainMenuIlSha256);
        return Invoke(screenManager, startMainMenu, Array.Empty<object?>(), "ensure-main-menu");
    }

    internal static L00CMenuActionReceipt CreateWorld(object screenManager, L00CScenarioTarget target,
        Action beginNativeOpen)
        => OpenWorld(screenManager, target, isNew: true, beginNativeOpen);

    // Reopens the exact A path through StartServerArgs, without retaining or
    // rebinding a GuiScreenSingleplayer menu entry/index.
    internal static L00CMenuActionReceipt ReopenWorld(object screenManager, L00CScenarioTarget target,
        Action beginNativeOpen)
        => OpenWorld(screenManager, target, isNew: false, beginNativeOpen);

    private static L00CMenuActionReceipt OpenWorld(object screenManager, L00CScenarioTarget target, bool isNew,
        Action beginNativeOpen)
    {
        RequireDebugLab();
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (beginNativeOpen is null) throw new ArgumentNullException(nameof(beginNativeOpen));
        string expectedPath = L00CScenarioDefinition.RequireCanonicalSavePath(target.CanonicalSavePath,
            "L00C_S2_NATIVE_TARGET_PATH_NOT_CANONICAL");
        Type argsType = RequireStartServerArgsType();
        MethodInfo connect = RequireScreenManagerMethod(screenManager, "ConnectToSingleplayer", 0x2138,
            new[] { argsType }, ConnectToSingleplayerIlSha256);
        object args = CreateStartServerArgs(argsType, target.Role, expectedPath, isNew,
            getter => ReadClientSetting(argsType, getter, getter switch
            {
                "get_PlayerName" => 0x282b,
                "get_DisabledMods" => 0x28c3,
                "get_ModPaths" => 0x28c1,
                "get_Language" => 0x2869,
                _ => throw new L00CScenarioException("L00C_S2_CLIENT_SETTING_UNEXPECTED", null, "getter=" + getter)
            }));
        ValidateStartServerArgs(args, expectedPath, isNew);
        beginNativeOpen();
        return Invoke(screenManager, connect, new[] { args }, (isNew ? "create-" : "reopen-") + target.Role);
    }

    // Ready is correlated with the independently forwarded LevelFinalize epoch,
    // then reduced to the exact native path/IsNew and canonical client GUID.
    internal static bool TryObserveReadySession(object screenManager, int expectedSessionOrdinal,
        L00CScenarioTarget expectedTarget, bool expectedIsNew, int levelFinalizeSessionOrdinal,
        out L00CScenarioObservation? observation)
    {
        RequireDebugLab();
        if (expectedTarget is null) throw new ArgumentNullException(nameof(expectedTarget));
        observation = null;
        if (levelFinalizeSessionOrdinal == 0) return false;
        if (levelFinalizeSessionOrdinal != expectedSessionOrdinal)
            throw new L00CScenarioException("L00C_S2_READY_SIGNAL_ORDINAL_MISMATCH", null,
                "expected=" + expectedSessionOrdinal + ";actual=" + levelFinalizeSessionOrdinal);
        if (!TryReadLiveSession(screenManager, out LiveSession? live) || live is null) return false;
        observation = L00CScenarioObservation.SessionReady(expectedSessionOrdinal, live.SavePath,
            live.SavegameGuid, live.IsNew, readyEventObserved: true);
        return true;
    }

    // Executes the audited native Save & Quit chain. The callback is the S3
    // integration boundary and runs after all guards, immediately before the
    // first native mutation.
    internal static L00CMenuActionReceipt SaveAndQuit(object screenManager, L00CScenarioSession expected,
        Action beginNativeReturn)
    {
        RequireDebugLab();
        if (expected is null) throw new ArgumentNullException(nameof(expected));
        if (beginNativeReturn is null) throw new ArgumentNullException(nameof(beginNativeReturn));
        GuardNativeShutdownAudit();
        if (!TryReadLiveSession(screenManager, out LiveSession? live) || live is null)
            throw new L00CScenarioException("L00C_S2_SAVEQUIT_SESSION_ABSENT", null, "live session is absent before Save&Quit");
        if (!SamePath(live.SavePath, expected.CanonicalSavePath))
            throw new L00CScenarioException("L00C_S2_SAVEQUIT_PATH_MISMATCH", null, "live StartServerArgs path differs from scenario session");
        if (!string.Equals(live.SavegameGuid, expected.CanonicalSavegameGuid, StringComparison.Ordinal) || live.IsNew != expected.IsNew)
            throw new L00CScenarioException("L00C_S2_SAVEQUIT_SESSION_IDENTITY_MISMATCH", null, "live GUID/IsNew differs from scenario session");
        if (!ReadIsServerRunning(screenManager))
            throw new L00CScenarioException("L00C_S2_SAVEQUIT_SERVER_NOT_RUNNING", null, "native single-player server is already stopped");

        Assembly lib = FindLoadedLib();
        Type mainType = RequireType(lib, "Vintagestory.Client.NoObf.ClientMain");
        MethodInfo sendLeave = RequireDeclaredMethod(mainType, "SendLeave", 0x24c4, new[] { typeof(int) });
        MethodInfo destroy = RequireDestroyGameSession(mainType);
        MethodInfo startMainMenu = RequireScreenManagerMethod(screenManager, "StartMainMenu", 0x2133, Type.EmptyTypes, StartMainMenuIlSha256);

        DateTimeOffset started = DateTimeOffset.UtcNow;
        beginNativeReturn();
        InvokeMethod(live.ClientMain, sendLeave, new object?[] { SaveQuitLeaveReason }, "L00C_S2_NATIVE_SENDLEAVE_FAILED");
        Type exitType = destroy.GetParameters()[1].ParameterType;
        InvokeMethod(live.ClientMain, destroy, new object?[] { false, Enum.Parse(exitType, "SoftExit") }, "L00C_S2_NATIVE_DESTROY_SESSION_FAILED");
        InvokeMethod(screenManager, startMainMenu, Array.Empty<object?>(), "L00C_S2_NATIVE_START_MENU_FAILED");
        return new L00CMenuActionReceipt("native-save-quit", started, DateTimeOffset.UtcNow,
            RequiredLibVersion, RequiredLibSha256,
            mainType.FullName + "::SendLeave;" + mainType.FullName + "::DestroyGameSession;Vintagestory.Client.ScreenManager::StartMainMenu", true);
    }

    // SaveCommitted is independent of LevelFinalize. The pinned 1.22.7
    // ServerThreadStart sets IsServerRunning=false only after ServerMain.Stop
    // returned; the exact save must then be non-empty and exclusively openable,
    // and StartMainMenu must have installed its audited main-screen instance.
    internal static bool TryObserveSaveCommitted(object screenManager, L00CScenarioSession expected,
        out L00CScenarioObservation? observation)
    {
        RequireDebugLab();
        if (expected is null) throw new ArgumentNullException(nameof(expected));
        observation = null;
        GuardNativeShutdownAudit();
        if (ReadIsServerRunning(screenManager)) return false;
        if (!IsMainMenuScreen(screenManager)) return false;
        try
        {
            using var stream = new FileStream(expected.CanonicalSavePath, FileMode.Open, FileAccess.Read, FileShare.None);
            if (stream.Length <= 0) return false;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        observation = L00CScenarioObservation.SaveCommitted(expected.Ordinal, expected.CanonicalSavePath,
            expected.CanonicalSavegameGuid, commitEventObserved: true);
        return true;
    }

    internal static bool TryObserveMenuReady(object screenManager)
    {
        RequireDebugLab();
        return !ReadIsServerRunning(screenManager) && IsMainMenuScreen(screenManager);
    }

    // Executable oracle seam against the real 1.22.7 argument type. It never
    // reads the user profile or starts a Vintage Story process.
    internal static object CreateStartServerArgsForOracle(Type type, string role, string savePath, bool isNew,
        object? playerName, IEnumerable<string> disabledMods, IEnumerable<string> clientModPaths, string language)
        => CreateStartServerArgs(type, role,
            L00CScenarioDefinition.RequireCanonicalSavePath(savePath, "L00C_S2_ORACLE_SAVE_PATH_NOT_CANONICAL"),
            isNew, getter => getter switch
            {
                "get_PlayerName" => playerName,
                "get_DisabledMods" => disabledMods,
                "get_ModPaths" => clientModPaths,
                "get_Language" => language,
                _ => throw new L00CScenarioException("L00C_S2_ORACLE_CLIENT_SETTING_UNEXPECTED", null, "getter=" + getter)
            });

    internal static void ValidateStartServerArgsForOracle(object args, string expectedPath, bool expectedIsNew)
        => ValidateStartServerArgs(args,
            L00CScenarioDefinition.RequireCanonicalSavePath(expectedPath, "L00C_S2_ORACLE_EXPECTED_PATH_NOT_CANONICAL"),
            expectedIsNew);

    // StartClientSide resolves the process manager once through this audited
    // chain, then releases the API lease. This is lifecycle plumbing, not a
    // graphical readiness signal.
    internal static L00CManagerResolution ResolveScreenManagerFromClientApi(object clientApi)
    {
        RequireDebugLab();
        Assembly lib = FindLoadedLib();
        RequireAuditedLibrary(lib);
        Type api = RequireType(lib, "Vintagestory.Client.NoObf.ClientCoreAPI");
        Type main = RequireType(lib, "Vintagestory.Client.NoObf.ClientMain");
        Type running = RequireType(lib, "Vintagestory.Client.GuiScreenRunningGame");
        Type guiScreen = RequireType(lib, "GuiScreen");
        Type manager = RequireType(lib, "Vintagestory.Client.ScreenManager");
        return ResolveClientSessionChain(clientApi, api, main, running, guiScreen, manager,
            0x11aa, 0x11f3, 0x0008);
    }

    internal static bool TryFindScreenManagerFromClientApi(object clientApi, out object? screenManager)
    {
        L00CManagerResolution resolution = ResolveScreenManagerFromClientApi(clientApi);
        screenManager = resolution.ScreenManager;
        return resolution.Status == L00CManagerResolutionStatus.Ready;
    }

    internal static bool TryResolveClientSessionChain(object clientApi, Type clientCoreApiType, Type clientMainType,
        Type runningGameType, Type guiScreenType, Type screenManagerType, int gameFieldToken,
        int runningGameFieldToken, int screenManagerFieldToken, out object? clientMain, out object? screenManager)
    {
        L00CManagerResolution result = ResolveClientSessionChain(clientApi, clientCoreApiType, clientMainType,
            runningGameType, guiScreenType, screenManagerType, gameFieldToken, runningGameFieldToken, screenManagerFieldToken);
        clientMain = result.ClientMain;
        screenManager = result.ScreenManager;
        return result.Status == L00CManagerResolutionStatus.Ready;
    }

    internal static L00CManagerResolution ResolveClientSessionChain(object clientApi, Type clientCoreApiType,
        Type clientMainType, Type runningGameType, Type guiScreenType, Type screenManagerType,
        int gameFieldToken, int runningGameFieldToken, int screenManagerFieldToken)
    {
        if (clientApi is null) throw new ArgumentNullException(nameof(clientApi));
        if (!clientCoreApiType.IsInstanceOfType(clientApi)) return new(L00CManagerResolutionStatus.ApiTypeMismatch, null, null);
        FieldInfo game = RequireDeclaredInstanceField(clientCoreApiType, "game", clientMainType, false, gameFieldToken);
        object? main = game.GetValue(clientApi);
        if (main is null || !clientMainType.IsInstanceOfType(main)) return new(L00CManagerResolutionStatus.GameUnavailable, null, null);
        FieldInfo running = RequireDeclaredInstanceField(clientMainType, "ScreenRunningGame", runningGameType, true, runningGameFieldToken);
        object? gameScreen = running.GetValue(main);
        if (gameScreen is null || !runningGameType.IsInstanceOfType(gameScreen))
            return new(L00CManagerResolutionStatus.RunningScreenUnavailable, main, null);
        if (runningGameType.BaseType != guiScreenType)
            throw new L00CScenarioException("L00C_S2_MANAGER_CHAIN_BASE_DRIFT", null, "GuiScreenRunningGame base type drifted");
        FieldInfo manager = RequireDeclaredInstanceField(guiScreenType, "ScreenManager", screenManagerType, true, screenManagerFieldToken);
        object? resolvedManager = manager.GetValue(gameScreen);
        if (resolvedManager is null || !screenManagerType.IsInstanceOfType(resolvedManager))
            return new(L00CManagerResolutionStatus.ManagerUnavailable, main, null);
        return new(L00CManagerResolutionStatus.Ready, main, resolvedManager);
    }

    internal static void EnqueueMainThreadTask(Action action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        RequireDebugLab();
        Assembly lib = FindLoadedLib();
        RequireAuditedLibrary(lib);
        Type manager = RequireType(lib, "Vintagestory.Client.ScreenManager");
        GuardIl(manager, "EnqueueMainThreadTask", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            new[] { typeof(Action) }, "2D0E0FEC4E3D47E083F2BEF081D24672E8CC03051C47BDBDAEC8CFB37FA7E977", 0x2118);
        MethodInfo enqueue = manager.GetMethod("EnqueueMainThreadTask", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(Action) }, null)!;
        InvokeMethod(null, enqueue, new object?[] { action }, "L00C_S2_MAIN_THREAD_ENQUEUE_FAILED");
    }

    private static object CreateStartServerArgs(Type type, string role, string savePath, bool isNew,
        Func<string, object?> readSetting)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (string.IsNullOrWhiteSpace(role))
            throw new L00CScenarioException("L00C_S2_NATIVE_ROLE_INVALID", null, "scenario role is required");
        object args = Activator.CreateInstance(type)
            ?? throw new L00CScenarioException("L00C_S2_STARTSERVERARGS_CONSTRUCTOR_INVALID", null, "parameterless constructor returned null");
        SetStartServerArgsField(args, "SaveFileLocation", savePath, 0x0f0e, true);
        SetStartServerArgsField(args, "DisabledMods", CloneStringList(readSetting("get_DisabledMods")), 0x0f17, false);
        SetStartServerArgsField(args, "ClientModPaths", CloneStringList(readSetting("get_ModPaths")), 0x0f19, false);
        SetStartServerArgsField(args, "Language", readSetting("get_Language"), 0x0f1a, true);
        SetStartServerArgsField(args, "IsNew", isNew, 0x0f1b, true);

        if (isNew)
        {
            SetStartServerArgsField(args, "Seed", "24681357", 0x0f0d, true);
            SetStartServerArgsField(args, "WorldName", "ISRWorldGen L00-C " + role, 0x0f0f, true);
            SetStartServerArgsField(args, "AllowCreativeMode", false, 0x0f10, true);
            SetStartServerArgsField(args, "PlayStyle", "surviveandbuild", 0x0f11, true);
            SetStartServerArgsField(args, "PlayStyleLangCode", "preset-surviveandbuild", 0x0f12, true);
            SetStartServerArgsField(args, "WorldType", "standard", 0x0f13, true);
            SetStartServerArgsField(args, "WorldConfiguration", CreateLaboratoryWorldConfiguration(type), 0x0f14, true);
            SetStartServerArgsField(args, "CreatedByPlayerName", readSetting("get_PlayerName"), 0x0f16, false);
        }
        ValidateStartServerArgs(args, savePath, isNew);
        return args;
    }

    private static void ValidateStartServerArgs(object args, string expectedPath, bool expectedIsNew)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        string actualPath = ReadStartServerArgsString(args, "SaveFileLocation", 0x0f0e);
        bool? actualIsNew = ReadStartServerArgsBool(args, "IsNew", 0x0f1b);
        if (!SamePath(actualPath, expectedPath))
            throw new L00CScenarioException("L00C_S2_STARTSERVERARGS_PATH_MISMATCH", null, "native args path differs from exact scenario target");
        if (actualIsNew != expectedIsNew)
            throw new L00CScenarioException("L00C_S2_STARTSERVERARGS_ISNEW_MISMATCH", null, "native args IsNew differs from planned action");
    }

    private static object CreateLaboratoryWorldConfiguration(Type startServerArgsType)
    {
        FieldInfo field = RequireStartServerArgsField(startServerArgsType, "WorldConfiguration", 0x0f14, true);
        Type jsonObject = field.FieldType;
        ConstructorInfo constructor = jsonObject.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(value => value.GetParameters().Length == 1 && value.GetParameters()[0].ParameterType.FullName == "Newtonsoft.Json.Linq.JToken")
            ?? throw new L00CScenarioException("L00C_S2_JSONOBJECT_CONTRACT_DRIFT", null, "JsonObject(JToken) constructor is absent");
        Type jToken = constructor.GetParameters()[0].ParameterType;
        Type jObject = jToken.Assembly.GetType("Newtonsoft.Json.Linq.JObject", false)
            ?? throw new L00CScenarioException("L00C_S2_JOBJECT_ABSENT", null, "Newtonsoft JObject is absent");
        MethodInfo parse = jObject.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null)
            ?? throw new L00CScenarioException("L00C_S2_JOBJECT_PARSE_DRIFT", null, "JObject.Parse(string) is absent");
        object token = parse.Invoke(null, new object?[] { "{\"worldWidth\":\"4096\",\"worldLength\":\"4096\",\"isrworldgenProfileId\":\"laboratory\"}" })
            ?? throw new L00CScenarioException("L00C_S2_JOBJECT_PARSE_NULL", null, "JObject.Parse returned null");
        object config = constructor.Invoke(new[] { token });
        PropertyInfo tokenProperty = jsonObject.GetProperty("Token", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new L00CScenarioException("L00C_S2_JSONOBJECT_TOKEN_DRIFT", null, "JsonObject.Token is absent");
        if (!ReferenceEquals(tokenProperty.GetValue(config), token))
            throw new L00CScenarioException("L00C_S2_JSONOBJECT_TOKEN_MISMATCH", null, "JsonObject did not retain its world configuration token");
        return config;
    }

    private static object? ReadClientSetting(Type startServerArgsType, string getter, int token)
    {
        Type settings = RequireType(startServerArgsType.Assembly, "Vintagestory.Client.NoObf.ClientSettings");
        MethodInfo method = settings.GetMethod(getter, BindingFlags.Static | BindingFlags.Public)
            ?? throw new L00CScenarioException("L00C_S2_CLIENT_SETTING_GETTER_DRIFT", null, getter + " is absent");
        if (method.MetadataToken != 0x06000000 + token || method.GetParameters().Length != 0)
            throw new L00CScenarioException("L00C_S2_CLIENT_SETTING_GETTER_DRIFT", null, getter + " token/signature changed");
        return method.Invoke(null, Array.Empty<object?>());
    }

    private static object CloneStringList(object? source)
    {
        if (source is not IEnumerable enumerable)
            throw new L00CScenarioException("L00C_S2_CLIENT_SETTING_LIST_ABSENT", null, "client string-list setting is absent");
        var values = new List<string>();
        foreach (object? item in enumerable)
        {
            if (item is not string text)
                throw new L00CScenarioException("L00C_S2_CLIENT_SETTING_LIST_DRIFT", null, "client setting contains a non-string value");
            values.Add(text);
        }
        return values;
    }

    private static bool TryReadLiveSession(object screenManager, out LiveSession? session)
    {
        Assembly lib = FindLoadedLib();
        RequireAuditedLibrary(lib);
        Type managerType = RequireType(lib, "Vintagestory.Client.ScreenManager");
        if (!managerType.IsInstanceOfType(screenManager))
            throw new L00CScenarioException("L00C_S2_SCREEN_MANAGER_TYPE_MISMATCH", null, "native adapter received another manager type");
        Type runningType = RequireType(lib, "Vintagestory.Client.GuiScreenRunningGame");
        Type mainType = RequireType(lib, "Vintagestory.Client.NoObf.ClientMain");
        Type argsType = RequireStartServerArgsType();
        FieldInfo current = RequireDeclaredInstanceField(managerType, "CurrentScreen", RequireType(lib, "GuiScreen"), false, 0x112a);
        object? currentScreen = current.GetValue(screenManager);
        if (currentScreen is null || !runningType.IsInstanceOfType(currentScreen)) { session = null; return false; }
        FieldInfo runningGame = RequireDeclaredInstanceField(runningType, "runningGame", mainType, false, 0x1070);
        FieldInfo serverArgs = RequireDeclaredInstanceField(runningType, "serverargs", argsType, true, 0x1073);
        object? main = runningGame.GetValue(currentScreen);
        object? args = serverArgs.GetValue(currentScreen);
        if (main is null || args is null) { session = null; return false; }
        string path = L00CScenarioDefinition.RequireCanonicalSavePath(
            ReadStartServerArgsString(args, "SaveFileLocation", 0x0f0e), "L00C_S2_LIVE_STARTSERVERARGS_PATH_NOT_CANONICAL");
        bool isNew = ReadStartServerArgsBool(args, "IsNew", 0x0f1b)
            ?? throw new L00CScenarioException("L00C_S2_LIVE_STARTSERVERARGS_ISNEW_ABSENT", null, "live IsNew is absent");
        string guid = ReadClientSavegameGuid(mainType, main);
        session = new LiveSession(main, path, guid, isNew);
        return true;
    }

    private static bool ReadIsServerRunning(object screenManager)
    {
        Assembly lib = FindLoadedLib();
        RequireAuditedLibrary(lib);
        Type managerType = RequireType(lib, "Vintagestory.Client.ScreenManager");
        if (!managerType.IsInstanceOfType(screenManager))
            throw new L00CScenarioException("L00C_S2_SCREEN_MANAGER_TYPE_MISMATCH", null, "server state received another manager type");
        Type platformType = RequireType(lib, "Vintagestory.Client.NoObf.ClientPlatformAbstract");
        FieldInfo platform = managerType.GetField("Platform", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            ?? throw new L00CScenarioException("L00C_S2_PLATFORM_FIELD_DRIFT", null, "ScreenManager.Platform is absent");
        if (platform.MetadataToken != 0x0400111d || platform.FieldType != platformType)
            throw new L00CScenarioException("L00C_S2_PLATFORM_FIELD_DRIFT", null, "ScreenManager.Platform token/type changed");
        object value = platform.GetValue(null)
            ?? throw new L00CScenarioException("L00C_S2_PLATFORM_ABSENT", null, "ScreenManager.Platform is null");
        PropertyInfo property = platformType.GetProperty("IsServerRunning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            ?? throw new L00CScenarioException("L00C_S2_SERVER_RUNNING_PROPERTY_DRIFT", null, "ClientPlatformAbstract.IsServerRunning is absent");
        if (property.PropertyType != typeof(bool) || property.GetMethod?.MetadataToken != 0x060026f9 || property.SetMethod?.MetadataToken != 0x060026fa)
            throw new L00CScenarioException("L00C_S2_SERVER_RUNNING_PROPERTY_DRIFT", null, "IsServerRunning type/token changed");
        return property.GetValue(value) is true;
    }

    private static bool IsMainMenuScreen(object screenManager)
    {
        Assembly lib = FindLoadedLib();
        RequireAuditedLibrary(lib);
        Type managerType = RequireType(lib, "Vintagestory.Client.ScreenManager");
        if (!managerType.IsInstanceOfType(screenManager))
            throw new L00CScenarioException("L00C_S2_SCREEN_MANAGER_TYPE_MISMATCH", null, "menu observation received another manager type");
        GuardIl(managerType, "StartMainMenu", PrivateInstance, Type.EmptyTypes, StartMainMenuIlSha256, 0x2133);
        Type guiScreen = RequireType(lib, "GuiScreen");
        Type mainScreen = RequireType(lib, "GuiScreenMainRight");
        FieldInfo current = RequireDeclaredInstanceField(managerType, "CurrentScreen", guiScreen, false, 0x112a);
        object? value = current.GetValue(screenManager);
        return value is not null && mainScreen.IsInstanceOfType(value);
    }

    private static void GuardNativeShutdownAudit()
    {
        Assembly lib = FindLoadedLib();
        RequireAuditedLibrary(lib);
        Type clientMain = RequireType(lib, "Vintagestory.Client.NoObf.ClientMain");
        Type clientProgram = RequireType(lib, "Vintagestory.Client.ClientProgram");
        Type serverMain = RequireType(lib, "Vintagestory.Server.ServerMain");
        GuardIl(clientMain, "DestroyGameSession", PrivateInstance, parameterCount: 2,
            DestroyGameSessionIlSha256, 0x24f4);
        GuardIl(clientProgram, "ServerThreadStart", PrivateInstance, Type.EmptyTypes,
            ServerThreadStartIlSha256, 0x1eaf);
        GuardIl(serverMain, "Stop", PrivateInstance, parameterCount: 4,
            ServerStopIlSha256, 0x10e8);
    }

    private static MethodInfo RequireScreenManagerMethod(object screenManager, string name, int token,
        Type[] parameters, string ilSha256)
    {
        Assembly lib = FindLoadedLib();
        RequireAuditedLibrary(lib);
        Type manager = RequireType(lib, "Vintagestory.Client.ScreenManager");
        if (!manager.IsInstanceOfType(screenManager))
            throw new L00CScenarioException("L00C_S2_SCREEN_MANAGER_TYPE_MISMATCH", null, "native adapter received another manager type");
        GuardIl(manager, name, PrivateInstance, parameters, ilSha256, token);
        return manager.GetMethod(name, PrivateInstance, null, parameters, null)!;
    }

    private static MethodInfo RequireDestroyGameSession(Type mainType)
    {
        MethodInfo destroy = mainType.GetMethods(PrivateInstance)
            .SingleOrDefault(method => method.Name == "DestroyGameSession" && method.GetParameters().Length == 2)
            ?? throw new L00CScenarioException("L00C_S2_DESTROY_SESSION_DRIFT", null, "DestroyGameSession(bool, SoftExit) is absent");
        ParameterInfo[] parameters = destroy.GetParameters();
        if (destroy.MetadataToken != 0x060024f4 || parameters[0].ParameterType != typeof(bool) ||
            !parameters[1].ParameterType.IsEnum || !Enum.GetNames(parameters[1].ParameterType).Contains("SoftExit", StringComparer.Ordinal))
            throw new L00CScenarioException("L00C_S2_DESTROY_SESSION_DRIFT", null, "DestroyGameSession signature/token changed");
        return destroy;
    }

    private static MethodInfo RequireDeclaredMethod(Type type, string name, int token, Type[] parameters)
    {
        MethodInfo method = type.GetMethod(name, PrivateInstance | BindingFlags.DeclaredOnly, null, parameters, null)
            ?? throw new L00CScenarioException("L00C_S2_NATIVE_METHOD_DRIFT", null, type.FullName + "." + name + " is absent");
        if (method.MetadataToken != 0x06000000 + token)
            throw new L00CScenarioException("L00C_S2_NATIVE_METHOD_DRIFT", null, type.FullName + "." + name + " token changed");
        return method;
    }

    private static void GuardIl(Type type, string methodName, BindingFlags flags, Type[] arguments,
        string expectedSha256, int token)
    {
        MethodInfo method = type.GetMethod(methodName, flags, null, arguments, null)
            ?? throw new L00CScenarioException("L00C_S2_NATIVE_METHOD_DRIFT", null, type.FullName + "." + methodName + " is absent");
        GuardMethodIl(method, expectedSha256, token);
    }

    private static void GuardIl(Type type, string methodName, BindingFlags flags, int parameterCount,
        string expectedSha256, int token)
    {
        MethodInfo method = type.GetMethods(flags).SingleOrDefault(value => value.Name == methodName && value.GetParameters().Length == parameterCount)
            ?? throw new L00CScenarioException("L00C_S2_NATIVE_METHOD_DRIFT", null, type.FullName + "." + methodName + "/" + parameterCount + " is absent");
        GuardMethodIl(method, expectedSha256, token);
    }

    private static void GuardMethodIl(MethodInfo method, string expectedSha256, int token)
    {
        if (method.MetadataToken != 0x06000000 + token)
            throw new L00CScenarioException("L00C_S2_NATIVE_METHOD_DRIFT", null, method.DeclaringType?.FullName + "." + method.Name + " token changed");
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            throw new L00CScenarioException("L00C_S2_NATIVE_METHOD_IL_ABSENT", null, method.DeclaringType?.FullName + "." + method.Name + " has no IL");
        using SHA256 sha = SHA256.Create();
        string actual = BitConverter.ToString(sha.ComputeHash(il)).Replace("-", string.Empty);
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
            throw new L00CScenarioException("L00C_S2_NATIVE_METHOD_IL_DRIFT", null, method.DeclaringType?.FullName + "." + method.Name + " IL changed");
    }

    private static FieldInfo RequireStartServerArgsField(Type type, string name, int token, bool publicField)
    {
        FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Type? expected = name switch
        {
            "Seed" or "SaveFileLocation" or "WorldName" or "PlayStyle" or "PlayStyleLangCode" or "WorldType" or "CreatedByPlayerName" or "Language" => typeof(string),
            "AllowCreativeMode" or "IsNew" => typeof(bool),
            "DisabledMods" or "ClientModPaths" => typeof(List<string>),
            "WorldConfiguration" => null,
            _ => throw new L00CScenarioException("L00C_S2_STARTSERVERARGS_FIELD_UNEXPECTED", null, "field=" + name)
        };
        if (field is null || field.IsStatic || field.IsPublic != publicField ||
            (expected is not null ? field.FieldType != expected : field.FieldType.FullName != "Vintagestory.API.Datastructures.JsonObject") ||
            (field.MetadataToken & 0x00ffffff) != token)
            throw new L00CScenarioException("L00C_S2_STARTSERVERARGS_FIELD_DRIFT", null, "field=" + name);
        return field;
    }

    private static void SetStartServerArgsField(object target, string name, object? value, int token, bool publicField)
    {
        FieldInfo field = RequireStartServerArgsField(target.GetType(), name, token, publicField);
        if (value is not null && !field.FieldType.IsInstanceOfType(value) && Nullable.GetUnderlyingType(field.FieldType) != value.GetType())
            throw new L00CScenarioException("L00C_S2_STARTSERVERARGS_FIELD_TYPE_MISMATCH", null, "field=" + name);
        field.SetValue(target, value);
    }

    private static string ReadStartServerArgsString(object args, string name, int token)
        => RequireStartServerArgsField(args.GetType(), name, token, true).GetValue(args) as string
           ?? throw new L00CScenarioException("L00C_S2_STARTSERVERARGS_STRING_ABSENT", null, "field=" + name);

    private static bool? ReadStartServerArgsBool(object args, string name, int token)
        => RequireStartServerArgsField(args.GetType(), name, token, true).GetValue(args) as bool?;

    private static string ReadClientSavegameGuid(Type mainType, object clientMain)
    {
        PropertyInfo? property = mainType.GetProperty("SavegameIdentifier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        MethodInfo? getter = property?.GetMethod;
        if (property?.PropertyType != typeof(string) || property.SetMethod is not null || getter is null ||
            getter.IsStatic || getter.MetadataToken != 0x0600247d || getter.GetParameters().Length != 0)
            throw new L00CScenarioException("L00C_S2_CLIENT_SAVE_GUID_GETTER_DRIFT", null, "ClientMain.SavegameIdentifier changed");
        string value = property.GetValue(clientMain) as string ?? string.Empty;
        return L00CScenarioObservation.RequireCanonicalGuid(value, "L00C_S2_CLIENT_SAVE_GUID_NOT_CANONICAL");
    }

    private static FieldInfo RequireDeclaredInstanceField(Type declaringType, string name, Type fieldType,
        bool isPublic, int expectedToken)
    {
        BindingFlags visibility = isPublic ? BindingFlags.Public : BindingFlags.NonPublic;
        FieldInfo? field = declaringType.GetField(name, BindingFlags.Instance | visibility | BindingFlags.DeclaredOnly);
        if (field is null || field.IsStatic || (isPublic ? !field.IsPublic : field.IsPublic) || field.FieldType != fieldType ||
            (field.MetadataToken & 0x00ffffff) != expectedToken)
            throw new L00CScenarioException("L00C_S2_NATIVE_FIELD_DRIFT", null, declaringType.FullName + "." + name + " changed");
        return field;
    }

    private static Type RequireStartServerArgsType()
        => RequireType(FindLoadedLib(), "Vintagestory.Common.StartServerArgs");

    private static void RequireAuditedLibrary(Assembly lib)
    {
        if (lib.GetName().Version?.ToString() != RequiredLibVersion || Hash(lib.Location) != RequiredLibSha256)
            throw new L00CScenarioException("L00C_S2_VINTAGESTORYLIB_DRIFT", null, "version or SHA-256 differs from audited 1.22.7 binary");
    }

    private static Type RequireType(Assembly assembly, string name)
        => assembly.GetType(name, false)
           ?? throw new L00CScenarioException("L00C_S2_NATIVE_TYPE_ABSENT", null, "type=" + name);

    private static L00CMenuActionReceipt Invoke(object? target, MethodInfo method, object?[] arguments, string action)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        InvokeMethod(target, method, arguments, "L00C_S2_NATIVE_ACTION_FAILED");
        return new L00CMenuActionReceipt(action, started, DateTimeOffset.UtcNow, RequiredLibVersion,
            RequiredLibSha256, method.DeclaringType?.FullName + "::" + method.Name, true);
    }

    private static object? InvokeMethod(object? target, MethodInfo method, object?[] arguments, string code)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException exception)
        {
            throw new L00CScenarioException(code, null, method.DeclaringType?.FullName + "." + method.Name + " failed",
                exception.InnerException ?? exception);
        }
    }

    private static void RequireDebugLab()
    {
#if !DEBUG
        throw new L00CScenarioException("L00C_S2_DEBUG_BUILD_REQUIRED", null, "native adapter is disabled outside Debug");
#else
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new L00CScenarioException("L00C_S2_DEBUG_LAB_AUTHORITY_REQUIRED", null, "Debugger.IsAttached and ISR_L00C_LAB=1 are both required");
#endif
    }

    private static Assembly FindLoadedLib()
        => AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == "VintagestoryLib")
           ?? throw new L00CScenarioException("L00C_S2_VINTAGESTORYLIB_NOT_LOADED", null, "VintagestoryLib is absent from the debugged client");

    private static string Hash(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static bool SamePath(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private sealed class LiveSession
    {
        internal LiveSession(object clientMain, string savePath, string savegameGuid, bool isNew)
        { ClientMain = clientMain; SavePath = savePath; SavegameGuid = savegameGuid; IsNew = isNew; }
        internal object ClientMain { get; }
        internal string SavePath { get; }
        internal string SavegameGuid { get; }
        internal bool IsNew { get; }
    }

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
}
