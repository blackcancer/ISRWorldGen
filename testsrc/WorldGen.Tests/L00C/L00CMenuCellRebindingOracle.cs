// Executable deterministic checks for the no-persisted-index policy.
#nullable enable
using System;
using System.IO;
using System.Reflection;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CMenuCellRebindingOracle
{
    private sealed class Entry { public string Filename = string.Empty; }
    private sealed class Screen { private readonly Entry[] entries; internal Screen(params string[] paths) { entries = Array.ConvertAll(paths, value => new Entry { Filename = value }); } }
    internal static int Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "l00c-rebind-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string target = Path.Combine(dir, "target.vcdbs"), other = Path.Combine(dir, "other.vcdbs"); File.WriteAllText(target, "t"); File.WriteAllText(other, "o");
            // Sort changes target from index 0 to index 1: current entries win.
            Check(Bind(new Screen(target, other), target) == 0); Check(Bind(new Screen(other, target), target) == 1);
            Expect(() => Bind(new Screen(other), target));
            Expect(() => Bind(new Screen(target, target), target));
            File.Delete(target); Expect(() => Bind(new Screen(target), target));
            return 0;
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    private static int Bind(object screen, string save)
    {
        MethodInfo method = typeof(L00CMenuActionLaboratoryHost).GetMethod("RebindCurrentCell", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("missing rebind method");
        try { return (int)method.Invoke(null, new object[] { screen, save })!; }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("L00-C rebinding oracle failed."); }
    private static void Expect(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("L00-C rebinding oracle expected refusal."); }
}
