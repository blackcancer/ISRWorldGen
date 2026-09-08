// Laboratory-only debugger helper for the literal client segment of T00-06.
// It is outside every production project: it never launches the game, changes a
// profile, or synthesizes UI input. Invoke it only from a VS debugger session.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace ISRWorldGen.L00C.Laboratory;

/// <summary>Outcome of the fixed ClientCoreAPI-to-ScreenManager chain.</summary>
internal enum L00CManagerResolutionStatus
{
    ApiTypeMismatch,
    GameUnavailable,
    RunningScreenUnavailable,
    ManagerUnavailable,
    Ready
}

/// <summary>Only <see cref="L00CManagerResolutionStatus.Ready"/> exposes a manager.</summary>
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
    public L00CMenuActionReceipt(string action, DateTimeOffset startedUtc, DateTimeOffset completedUtc,
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

    public string Action { get; private set; }
    public DateTimeOffset StartedUtc { get; private set; }
    public DateTimeOffset CompletedUtc { get; private set; }
    public string VintagestoryLibVersion { get; private set; }
    public string VintagestoryLibSha256 { get; private set; }
    public string TargetMethod { get; private set; }
    public bool Completed { get; private set; }
}

/// <summary>Version/hash locked reflection bridge for the audited 1.22.7 client menu actions.</summary>
internal static class L00CMenuActionDriver
{
    internal const string RequiredLibVersion = "1.22.7.0";
    internal const string RequiredLibSha256 = "E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0";
    internal const int SaveQuitLeaveReason = 0;

    internal static L00CMenuActionReceipt EnterSingleplayerMenu(object mainMenuLeft)
    {
        GuardTarget(mainMenuLeft, "Vintagestory.Client.GuiCompositeMainMenuLeft", "OnSingleplayer", Type.EmptyTypes);
        return Invoke(mainMenuLeft, "OnSingleplayer", Array.Empty<object?>(), "enter-singleplayer-menu", true);
    }

    internal static L00CMenuActionReceipt ReopenPrimaryWorld(object singleplayerScreen, int saveCellIndex)
    {
        GuardTarget(singleplayerScreen, "Vintagestory.Client.GuiScreenSingleplayer", "OnClickCellLeft", new[] { typeof(int) });
        return Invoke(singleplayerScreen, "OnClickCellLeft", new object?[] { saveCellIndex }, "reopen-primary-world", false);
    }

    /// <summary>Runs the audited native new-world connection chain.  It never writes a save itself.</summary>
    internal static L00CMenuActionReceipt CreateFixtureWorld(object screenManager, string role, string savePath)
    {
        RequireDebugLab();
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(savePath)) throw new ArgumentException("L00-C fixture role and save path are required.");
        GuardTarget(screenManager, "Vintagestory.Client.ScreenManager", "ConnectToSingleplayer",
            new[] { RequireStartServerArgsType() });
        object args = CreateStartServerArgs(role, savePath);
        return Invoke(screenManager, "ConnectToSingleplayer", new[] { args }, "create-" + role + "-world", false);
    }

    internal static L00CMenuActionReceipt ReturnToMainMenu(object clientMain, object screenManager)
    {
        // All type/version/hash/method checks run before SendLeave or any session mutation.
        GuardTarget(clientMain, "Vintagestory.Client.NoObf.ClientMain", "SendLeave", new[] { typeof(int) });
        MethodInfo destroy = GuardDestroyGameSession(clientMain);
        GuardTarget(screenManager, "Vintagestory.Client.ScreenManager", "StartMainMenu", Type.EmptyTypes);
        // Same shutdown chain as Save & Quit: SendLeave(0) → DestroyGameSession(false, SoftExit) → StartMainMenu.
        InvokeExact(clientMain, "SendLeave", new object?[] { SaveQuitLeaveReason });
        Type exitType = destroy.GetParameters()[1].ParameterType;
        InvokeMethod(clientMain, destroy, new object?[] { false, Enum.Parse(exitType, "SoftExit") });
        return Invoke(screenManager, "StartMainMenu", Array.Empty<object?>(), "return-main-menu", false);
    }

    // Discovery is deliberately kept beside the guarded internal actions.  The host uses
    // only ICoreClientAPI lifecycle callbacks and never binds a game-private type.
    internal static bool TryFindMenuLeft(object clientApi, out object? menuLeft)
        => TryFindReachable(clientApi, "Vintagestory.Client.GuiCompositeMainMenuLeft", out menuLeft);

    internal static bool TryFindSingleplayerScreen(object clientApi, out object? singleplayer)
        => TryFindReachable(clientApi, "Vintagestory.Client.GuiScreenSingleplayer", out singleplayer);

    internal static bool TryFindClientSession(object clientApi, out object? clientMain, out object? screenManager)
    {
        bool main = TryFindReachable(clientApi, "Vintagestory.Client.NoObf.ClientMain", out clientMain);
        bool manager = TryFindReachable(clientApi, "Vintagestory.Client.ScreenManager", out screenManager);
        return main && manager;
    }

    // StartClientSide receives ClientCoreAPI.  Resolve its process-wide manager
    // through exactly this audited field chain; later campaign states instead
    // begin at the manager and retain their existing generic session discovery.
    /// <summary>
    /// Resolves the manager through the only audited chain.  A partially built
    /// client is a normal, retryable lifecycle state; a reflection mismatch is
    /// still an exception and is deliberately never retried.
    /// </summary>
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

    // Retained for the original local oracle and callers that only need the
    // successful object. New lifecycle code must consume the explicit status.
    internal static bool TryFindScreenManagerFromClientApi(object clientApi, out object? screenManager)
    {
        L00CManagerResolution resolution = ResolveScreenManagerFromClientApi(clientApi);
        screenManager = resolution.ScreenManager;
        return resolution.Status == L00CManagerResolutionStatus.Ready;
    }

    // ScreenManager owns the process-wide main-thread queue.  This is deliberately
    // separate from an ICoreClientAPI listener: DestroyGameSession disposes a
    // session's mod systems, but OnNewFrame continues to drain this queue.
    internal static void EnqueueMainThreadTask(Action action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        GuardMainThreadPump();
        MethodInfo enqueue = FindLoadedLib().GetType("Vintagestory.Client.ScreenManager", true)!
            .GetMethod("EnqueueMainThreadTask", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Action) }, null)
            ?? throw new InvalidOperationException("L00-C menu POC refused: ScreenManager.EnqueueMainThreadTask(Action) drifted.");
        InvokeMethod(null, enqueue, new object?[] { action });
    }

    internal static bool TryFindCurrentSingleplayerScreen(object screenManager, out object? screen)
    {
        RequireDebugLab();
        GuardTarget(screenManager, "Vintagestory.Client.ScreenManager", "StartMainMenu", Type.EmptyTypes);
        Type managerType = screenManager.GetType();
        FieldInfo current = managerType.GetField("CurrentScreen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("L00-C menu POC refused: ScreenManager.CurrentScreen drifted.");
        object? candidate = current.GetValue(screenManager);
        Type expected = FindLoadedLib().GetType("Vintagestory.Client.GuiScreenSingleplayer", true)!;
        if (candidate is not null && expected.IsInstanceOfType(candidate)) { screen = candidate; return true; }
        screen = null; return false;
    }

    private static L00CMenuActionReceipt Invoke(object target, string name, object?[] arguments, string action, bool expectTrue)
    {
        RequireDebugLab();
        MethodInfo method = FindExact(target.GetType(), name, arguments.Select(a => a?.GetType() ?? throw new InvalidOperationException("Null argument is forbidden.")).ToArray());
        DateTimeOffset started = DateTimeOffset.UtcNow;
        object? result = InvokeMethod(target, method, arguments);
        if (expectTrue && result is not true) throw new InvalidOperationException("L00-C menu POC refused: OnSingleplayer did not report success.");
        return new(action, started, DateTimeOffset.UtcNow, RequiredLibVersion, RequiredLibSha256,
            $"{method.DeclaringType?.FullName}::{method.Name}", true);
    }

    private static void Guard(string typeName, string methodName, Type[] parameters)
    {
        RequireDebugLab();
        Assembly lib = FindLoadedLib();
        if (lib.GetName().Version?.ToString() != RequiredLibVersion || Hash(lib.Location) != RequiredLibSha256)
            throw new InvalidOperationException("L00-C menu POC refused: VintagestoryLib version or SHA-256 drifted from the audited target.");
        Type type = lib.GetType(typeName, false) ?? throw new InvalidOperationException($"L00-C menu POC refused: {typeName} is absent.");
        _ = type.GetMethod(methodName, PrivateInstance, null, parameters, null)
            ?? throw new InvalidOperationException($"L00-C menu POC refused: {typeName}.{methodName} is absent.");
    }

    private static void GuardTarget(object target, string typeName, string methodName, Type[] parameters)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        Guard(typeName, methodName, parameters);
        Type auditedType = FindLoadedLib().GetType(typeName, false)
            ?? throw new InvalidOperationException($"L00-C menu POC refused: {typeName} is absent.");
        if (!auditedType.IsInstanceOfType(target))
            throw new InvalidOperationException($"L00-C menu POC refused: supplied target is not an instance of audited {typeName}.");
    }

    private static MethodInfo GuardDestroyGameSession(object clientMain)
    {
        MethodInfo destroy = clientMain.GetType().GetMethods(PrivateInstance)
            .SingleOrDefault(m => m.Name == "DestroyGameSession" && m.GetParameters().Length == 2)
            ?? throw new InvalidOperationException("L00-C menu POC refused: ClientMain.DestroyGameSession(bool, SoftExit) was not found.");
        ParameterInfo[] parameters = destroy.GetParameters();
        Type exitType = parameters[1].ParameterType;
        if (parameters[0].ParameterType != typeof(bool) || !exitType.IsEnum || !Enum.GetNames(exitType).Contains("SoftExit", StringComparer.Ordinal))
            throw new InvalidOperationException("L00-C menu POC refused: ClientMain.DestroyGameSession(bool, SoftExit) drifted.");
        return destroy;
    }

    private static void RequireDebugLab()
    {
        if (!Debugger.IsAttached) throw new InvalidOperationException("L00-C menu POC requires a debugger to be attached; debugger origin is not inferred.");
        if (!string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C menu POC requires ISR_L00C_LAB=1 in addition to an attached debugger.");
    }

    private static void GuardMainThreadPump()
    {
        RequireDebugLab();
        Assembly lib = FindLoadedLib();
        if (lib.GetName().Version?.ToString() != RequiredLibVersion || Hash(lib.Location) != RequiredLibSha256)
            throw new InvalidOperationException("L00-C menu POC refused: VintagestoryLib version or SHA-256 drifted from the audited target.");
        Type manager = lib.GetType("Vintagestory.Client.ScreenManager", true)!;
        GuardIl(manager, "EnqueueMainThreadTask", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            new[] { typeof(Action) }, "2D0E0FEC4E3D47E083F2BEF081D24672E8CC03051C47BDBDAEC8CFB37FA7E977");
        GuardIl(manager, "OnNewFrame", PrivateInstance, new[] { typeof(float) },
            "EEFC023F05EF3E3E1FAA5F06D3EB6C1996812827854F67532E5AA34CEB1B389E");
    }

    private static void GuardIl(Type type, string methodName, BindingFlags flags, Type[] arguments, string expectedSha256)
    {
        MethodInfo method = type.GetMethod(methodName, flags, null, arguments, null)
            ?? throw new InvalidOperationException("L00-C menu POC refused: " + type.FullName + "." + methodName + " drifted.");
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) throw new InvalidOperationException("L00-C menu POC refused: " + type.FullName + "." + methodName + " has no auditable IL.");
        using SHA256 sha = SHA256.Create();
        string actual = BitConverter.ToString(sha.ComputeHash(il)).Replace("-", string.Empty);
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C menu POC refused: " + type.FullName + "." + methodName + " IL drifted.");
    }

    private static Type RequireStartServerArgsType()
        => FindLoadedLib().GetType("Vintagestory.Common.StartServerArgs", false)
           ?? throw new InvalidOperationException("L00-C bootstrap refused: StartServerArgs is absent.");

    private static object CreateStartServerArgs(string role, string savePath)
    {
        Type type = RequireStartServerArgsType();
        object args = Activator.CreateInstance(type) ?? throw new InvalidOperationException("L00-C bootstrap refused: StartServerArgs has no usable parameterless constructor.");
        SetPublicField(args, "Seed", "24681357");
        SetPublicField(args, "SaveFileLocation", Path.GetFullPath(savePath));
        SetPublicField(args, "WorldName", "ISRWorldGen L00-C " + role);
        SetPublicField(args, "AllowCreativeMode", false);
        SetPublicField(args, "PlayStyle", "surviveandbuild");
        SetPublicField(args, "WorldType", "standard");
        SetPublicField(args, "MapSizeY", (int?)256);
        SetPublicField(args, "IsNew", true);
        return args;
    }

    private static void SetPublicField(object target, string name, object? value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("L00-C bootstrap refused: StartServerArgs." + name + " drifted.");
        if (value is not null && !field.FieldType.IsInstanceOfType(value) && Nullable.GetUnderlyingType(field.FieldType) != value.GetType())
            throw new InvalidOperationException("L00-C bootstrap refused: StartServerArgs." + name + " has an unexpected type.");
        field.SetValue(target, value);
    }

    // This is intentionally a fixed audited chain, not an object-graph search:
    // ClientCoreAPI.game -> ClientMain.ScreenRunningGame -> GuiScreen.ScreenManager.
    // Tokens are checked as well as name, declaring type, visibility, instance-ness
    // and field type so an API update fails before it can target another session.
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

    internal static L00CManagerResolution ResolveClientSessionChain(object clientApi, Type clientCoreApiType, Type clientMainType,
        Type runningGameType, Type guiScreenType, Type screenManagerType, int gameFieldToken,
        int runningGameFieldToken, int screenManagerFieldToken)
    {
        if (clientApi is null) throw new ArgumentNullException(nameof(clientApi));
        if (!clientCoreApiType.IsInstanceOfType(clientApi)) return new(L00CManagerResolutionStatus.ApiTypeMismatch, null, null);

        FieldInfo game = RequireDeclaredInstanceField(clientCoreApiType, "game", clientMainType, false, gameFieldToken);
        object? main = game.GetValue(clientApi);
        if (main is null || !clientMainType.IsInstanceOfType(main)) return new(L00CManagerResolutionStatus.GameUnavailable, null, null);

        FieldInfo running = RequireDeclaredInstanceField(clientMainType, "ScreenRunningGame", runningGameType, true, runningGameFieldToken);
        object? gameScreen = running.GetValue(main);
        if (gameScreen is null || !runningGameType.IsInstanceOfType(gameScreen)) return new(L00CManagerResolutionStatus.RunningScreenUnavailable, main, null);

        if (runningGameType.BaseType != guiScreenType)
            throw new InvalidOperationException("L00-C menu POC refused: GuiScreenRunningGame no longer directly inherits GuiScreen.");
        FieldInfo manager = RequireDeclaredInstanceField(guiScreenType, "ScreenManager", screenManagerType, true, screenManagerFieldToken);
        object? resolvedManager = manager.GetValue(gameScreen);
        if (resolvedManager is null || !screenManagerType.IsInstanceOfType(resolvedManager)) return new(L00CManagerResolutionStatus.ManagerUnavailable, main, null);

        return new(L00CManagerResolutionStatus.Ready, main, resolvedManager);
    }

    private static FieldInfo RequireDeclaredInstanceField(Type declaringType, string name, Type fieldType, bool isPublic, int expectedToken)
    {
        BindingFlags visibility = isPublic ? BindingFlags.Public : BindingFlags.NonPublic;
        FieldInfo? field = declaringType.GetField(name, BindingFlags.Instance | visibility | BindingFlags.DeclaredOnly);
        // The audit records the FieldDef row-id (the low 24 bits); reflection
        // exposes that id prefixed by the ECMA-335 FieldDef token table (0x04).
        if (field is null || field.IsStatic || (isPublic ? !field.IsPublic : !field.IsPrivate) || field.FieldType != fieldType ||
            (field.MetadataToken & 0x00ffffff) != expectedToken)
            throw new InvalidOperationException("L00-C menu POC refused: audited " + declaringType.FullName + "." + name + " field drifted.");
        return field;
    }

    private static void RequireAuditedLibrary(Assembly lib)
    {
        if (lib.GetName().Version?.ToString() != RequiredLibVersion || Hash(lib.Location) != RequiredLibSha256)
            throw new InvalidOperationException("L00-C menu POC refused: VintagestoryLib version or SHA-256 drifted from the audited target.");
    }

    private static Type RequireType(Assembly assembly, string name)
        => assembly.GetType(name, false) ?? throw new InvalidOperationException("L00-C menu POC refused: " + name + " is absent.");

    private static bool TryFindReachable(object root, string auditedTypeName, out object? target)
    {
        RequireDebugLab();
        Assembly lib = FindLoadedLib();
        if (lib.GetName().Version?.ToString() != RequiredLibVersion || Hash(lib.Location) != RequiredLibSha256)
            throw new InvalidOperationException("L00-C menu POC refused: VintagestoryLib version or SHA-256 drifted from the audited target.");
        Type audited = lib.GetType(auditedTypeName, true)!;
        var pending = new System.Collections.Generic.Queue<(object Value, int Depth)>();
        pending.Enqueue((root, 0));
        while (pending.Count > 0)
        {
            (object value, int depth) = pending.Dequeue();
            if (audited.IsInstanceOfType(value)) { target = value; return true; }
            if (depth == 2) continue;
            foreach (FieldInfo field in value.GetType().GetFields(PrivateInstance))
                if (!field.FieldType.IsValueType && field.GetValue(value) is object child) pending.Enqueue((child, depth + 1));
            foreach (PropertyInfo property in value.GetType().GetProperties(PrivateInstance))
                if (property.GetIndexParameters().Length == 0 && property.CanRead && !property.PropertyType.IsValueType)
                    try { if (property.GetValue(value) is object child) pending.Enqueue((child, depth + 1)); } catch { }
        }
        target = null; return false;
    }

    private static Assembly FindLoadedLib() => AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "VintagestoryLib")
        ?? throw new InvalidOperationException("L00-C menu POC refused: VintagestoryLib is not loaded by the debugged client.");
    private static MethodInfo FindExact(Type type, string name, Type[] types) => type.GetMethod(name, PrivateInstance, null, types, null)
        ?? throw new InvalidOperationException($"L00-C menu POC refused: {type.FullName}.{name} has drifted.");
    private static object? InvokeExact(object target, string name, object?[] arguments) => InvokeMethod(target, FindExact(target.GetType(), name, arguments.Select(a => a!.GetType()).ToArray()), arguments);
    private static object? InvokeMethod(object? target, MethodInfo method, object?[] arguments)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException ex) { throw new InvalidOperationException($"L00-C menu POC action {method.Name} failed.", ex.InnerException ?? ex); }
    }
    private static string Hash(string path) { using SHA256 sha = SHA256.Create(); using FileStream stream = File.OpenRead(path); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty); }
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
}
