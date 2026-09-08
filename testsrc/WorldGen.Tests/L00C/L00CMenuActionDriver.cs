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

public sealed class L00CMenuActionReceipt
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
public static class L00CMenuActionDriver
{
    public const string RequiredLibVersion = "1.22.7.0";
    public const string RequiredLibSha256 = "E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0";
    public const int SaveQuitLeaveReason = 0;

    public static L00CMenuActionReceipt EnterSingleplayerMenu(object mainMenuLeft)
    {
        Guard("Vintagestory.Client.GuiCompositeMainMenuLeft", "OnSingleplayer", Type.EmptyTypes);
        return Invoke(mainMenuLeft, "OnSingleplayer", Array.Empty<object?>(), "enter-singleplayer-menu", true);
    }

    public static L00CMenuActionReceipt ReopenPrimaryWorld(object singleplayerScreen, int saveCellIndex)
    {
        Guard("Vintagestory.Client.GuiScreenSingleplayer", "OnClickCellLeft", new[] { typeof(int) });
        return Invoke(singleplayerScreen, "OnClickCellLeft", new object?[] { saveCellIndex }, "reopen-primary-world", false);
    }

    public static L00CMenuActionReceipt ReturnToMainMenu(object clientMain, object screenManager)
    {
        RequireDebugLab();
        // Same shutdown chain as Save & Quit: SendLeave(0) → DestroyGameSession(false, SoftExit) → StartMainMenu.
        InvokeExact(clientMain, "SendLeave", new object?[] { SaveQuitLeaveReason });
        MethodInfo destroy = clientMain.GetType().GetMethods(PrivateInstance)
            .SingleOrDefault(m => m.Name == "DestroyGameSession" && m.GetParameters().Length == 2)
            ?? throw new InvalidOperationException("L00-C menu POC refused: ClientMain.DestroyGameSession(bool, SoftExit) was not found.");
        Type exitType = destroy.GetParameters()[1].ParameterType;
        if (!exitType.IsEnum || !Enum.GetNames(exitType).Contains("SoftExit", StringComparer.Ordinal))
            throw new InvalidOperationException("L00-C menu POC refused: the audited SoftExit enum member was not found.");
        InvokeMethod(clientMain, destroy, new object?[] { false, Enum.Parse(exitType, "SoftExit") });
        Guard("Vintagestory.Client.ScreenManager", "StartMainMenu", Type.EmptyTypes);
        return Invoke(screenManager, "StartMainMenu", Array.Empty<object?>(), "return-main-menu", false);
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

    private static void RequireDebugLab()
    {
        if (!Debugger.IsAttached) throw new InvalidOperationException("L00-C menu POC refuses execution outside the Visual Studio debugger.");
        if (!string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C menu POC refuses execution without ISR_L00C_LAB=1.");
    }

    private static Assembly FindLoadedLib() => AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "VintagestoryLib")
        ?? throw new InvalidOperationException("L00-C menu POC refused: VintagestoryLib is not loaded by the debugged client.");
    private static MethodInfo FindExact(Type type, string name, Type[] types) => type.GetMethod(name, PrivateInstance, null, types, null)
        ?? throw new InvalidOperationException($"L00-C menu POC refused: {type.FullName}.{name} has drifted.");
    private static object? InvokeExact(object target, string name, object?[] arguments) => InvokeMethod(target, FindExact(target.GetType(), name, arguments.Select(a => a!.GetType()).ToArray()), arguments);
    private static object? InvokeMethod(object target, MethodInfo method, object?[] arguments)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException ex) { throw new InvalidOperationException($"L00-C menu POC action {method.Name} failed.", ex.InnerException ?? ex); }
    }
    private static string Hash(string path) { using SHA256 sha = SHA256.Create(); using FileStream stream = File.OpenRead(path); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty); }
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
}
