// Executable reflection oracle for the Debug-linked L00-C fixture helper.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CNativeFixtureOracle
{
    internal static int Run()
    {
        string gamePath = Environment.GetEnvironmentVariable("ISR_L00C_ORACLE_GAME_PATH")
            ?? throw new InvalidOperationException("L00-C oracle game path is absent.");
        _ = Assembly.LoadFrom(Path.Combine(gamePath, "Lib", "Newtonsoft.Json.dll"));
        _ = Assembly.LoadFrom(Path.Combine(gamePath, "VintagestoryAPI.dll"));
        Assembly lib = Assembly.LoadFrom(Path.Combine(gamePath, "VintagestoryLib.dll"));
        Type argsType = lib.GetType("Vintagestory.Common.StartServerArgs", true)!;
        var disabled = new List<string> { "example-disabled" };
        var paths = new List<string> { @"E:\laboratory-mod-path" };
        string save = Path.Combine(Path.GetTempPath(), "l00c-native-fixture-oracle.vcdbs");
        object args = L00CMenuActionDriver.CreateStartServerArgsForOracle(argsType, "iteration-01-a", save, true,
            "oracle-player", disabled, paths, "en");
        Require("Seed", "24681357", args); Require("SaveFileLocation", Path.GetFullPath(save), args);
        Require("WorldName", "ISRWorldGen L00-C iteration-01-a", args); Require("PlayStyle", "surviveandbuild", args);
        Require("PlayStyleLangCode", "preset-surviveandbuild", args); Require("WorldType", "standard", args);
        Require("CreatedByPlayerName", "oracle-player", args); Require("Language", "en", args);
        if (Read(args, "AllowCreativeMode") is not false || Read(args, "IsNew") is not true)
            throw new InvalidOperationException("Native boolean mapping mismatch.");
        RequireListCopy(args, "DisabledMods", disabled); RequireListCopy(args, "ClientModPaths", paths);
        object config = Read(args, "WorldConfiguration") ?? throw new InvalidOperationException("WorldConfiguration is null.");
        object token = config.GetType().GetProperty("Token")?.GetValue(config) ?? throw new InvalidOperationException("Jworldconfig token is null.");
        PropertyInfo indexer = token.GetType().GetProperty("Item", new[] { typeof(string) }) ?? throw new InvalidOperationException("JObject indexer absent.");
        RequireToken(indexer, token, "worldWidth", "4096"); RequireToken(indexer, token, "worldLength", "4096"); RequireToken(indexer, token, "isrworldgenProfileId", "laboratory");
        L00CMenuActionDriver.ValidateStartServerArgsForOracle(args, Path.GetFullPath(save), true);

        object reopen = L00CMenuActionDriver.CreateStartServerArgsForOracle(argsType, "iteration-01-a", save, false,
            "ignored-for-reopen", disabled, paths, "en");
        Require("SaveFileLocation", Path.GetFullPath(save), reopen); Require("Language", "en", reopen);
        if (Read(reopen, "IsNew") is not false || Read(reopen, "Seed") is not null ||
            Read(reopen, "WorldName") is not null || Read(reopen, "WorldConfiguration") is not null ||
            Read(reopen, "CreatedByPlayerName") is not null)
            throw new InvalidOperationException("Native reopen StartServerArgs does not match the exact OnClickCellLeft mapping.");
        RequireListCopy(reopen, "DisabledMods", disabled); RequireListCopy(reopen, "ClientModPaths", paths);
        L00CMenuActionDriver.ValidateStartServerArgsForOracle(reopen, Path.GetFullPath(save), false);

        Set(reopen, "SaveFileLocation", Path.Combine(Path.GetTempPath(), "another-save.vcdbs"));
        RequireCode(() => L00CMenuActionDriver.ValidateStartServerArgsForOracle(reopen, Path.GetFullPath(save), false),
            "L00C_S2_STARTSERVERARGS_PATH_MISMATCH");
        Set(reopen, "SaveFileLocation", Path.GetFullPath(save)); Set(reopen, "IsNew", true);
        RequireCode(() => L00CMenuActionDriver.ValidateStartServerArgsForOracle(reopen, Path.GetFullPath(save), false),
            "L00C_S2_STARTSERVERARGS_ISNEW_MISMATCH");
        RequireCode(() => L00CMenuActionDriver.CreateStartServerArgsForOracle(argsType, "iteration-01-a",
            @"relative\save.vcdbs", false, null, disabled, paths, "en"), "L00C_S2_ORACLE_SAVE_PATH_NOT_CANONICAL");
        return 0;
    }

    private static object? Read(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
    private static void Set(object target, string name, object? value) => (target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Native field absent: " + name)).SetValue(target, value);
    private static void Require(string name, string expected, object target) { if (!string.Equals(Read(target, name) as string, expected, StringComparison.Ordinal)) throw new InvalidOperationException("Native argument mapping mismatch: " + name); }
    private static void RequireListCopy(object args, string name, List<string> expected)
    {
        if (Read(args, name) is not List<string> actual || ReferenceEquals(actual, expected) || actual.Count != expected.Count || actual[0] != expected[0])
            throw new InvalidOperationException("Native list mapping mismatch: " + name);
    }
    private static void RequireToken(PropertyInfo indexer, object token, string name, string expected)
    {
        if (!string.Equals(indexer.GetValue(token, new object[] { name })?.ToString(), expected, StringComparison.Ordinal))
            throw new InvalidOperationException("WorldConfiguration token mismatch: " + name);
    }
    private static void RequireCode(Action action, string code)
    {
        try { action(); }
        catch (L00CScenarioException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException("Expected exact diagnostic code " + code + ".");
    }
}
