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
        object args = L00CMenuActionDriver.CreateStartServerArgsForOracle(argsType, "activated-primary", save,
            "oracle-player", disabled, paths, "en");
        Require("Seed", "24681357", args); Require("SaveFileLocation", Path.GetFullPath(save), args);
        Require("WorldName", "ISRWorldGen L00-C activated-primary", args); Require("PlayStyle", "surviveandbuild", args);
        Require("PlayStyleLangCode", "preset-surviveandbuild", args); Require("WorldType", "standard", args);
        Require("CreatedByPlayerName", "oracle-player", args); Require("Language", "en", args);
        if (Read(args, "AllowCreativeMode") is not false || Read(args, "IsNew") is not true || Read(args, "MapSizeY") is not int y || y != 256)
            throw new InvalidOperationException("Native boolean/height mapping mismatch.");
        RequireListCopy(args, "DisabledMods", disabled); RequireListCopy(args, "ClientModPaths", paths);
        object config = Read(args, "WorldConfiguration") ?? throw new InvalidOperationException("WorldConfiguration is null.");
        object token = config.GetType().GetProperty("Token")?.GetValue(config) ?? throw new InvalidOperationException("Jworldconfig token is null.");
        PropertyInfo indexer = token.GetType().GetProperty("Item", new[] { typeof(string) }) ?? throw new InvalidOperationException("JObject indexer absent.");
        RequireToken(indexer, token, "worldWidth", "4096"); RequireToken(indexer, token, "worldLength", "4096"); RequireToken(indexer, token, "isrworldgenProfileId", "laboratory");
        if (!L00CMenuActionDriver.IsNewWorldReadinessSatisfied(true, true, true, true, true, true, true, true, true, true))
            throw new InvalidOperationException("Fully proven readiness was refused.");
        for (int i = 0; i < 10; i++)
        {
            bool[] values = { true, true, true, true, true, true, true, true, true, true };
            values[i] = false;
            if (L00CMenuActionDriver.IsNewWorldReadinessSatisfied(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9]))
                throw new InvalidOperationException("False readiness permitted return at predicate index " + i + ".");
        }
        return 0;
    }

    private static object? Read(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
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
}
