// Debug-only native fixture bootstrap.  It invokes the client creation chain and only
// publishes a cell marker after both the real save and its live menu cell are observable.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

internal sealed class L00CFixtureBootstrap
{
    private readonly string root;
    private readonly string evidence;
    private readonly Fixture primary;
    private readonly Fixture secondary;
    private int stableTicks;
    private bool bindingMenuRequested;
    private State state = State.WaitPrimaryMenu;

    internal L00CFixtureBootstrap(string laboratoryRoot, string evidenceDirectory)
    {
        RequireDebugLaboratory();
        root = CanonicalLaboratoryRoot(laboratoryRoot);
        if (!Directory.Exists(root)) throw new InvalidOperationException("L00-C bootstrap requires the existing repository .local\\L00C root.");
        if (Directory.Exists(evidenceDirectory) || File.Exists(evidenceDirectory)) throw new InvalidOperationException("L00-C bootstrap evidence must be new.");
        evidence = Path.GetFullPath(evidenceDirectory);
        if (!IsUnder(evidence, root)) throw new InvalidOperationException("L00-C bootstrap evidence must remain under its laboratory root.");
        Directory.CreateDirectory(evidence);
        string saves = Path.Combine(root, "saves"); Directory.CreateDirectory(saves);
        primary = new Fixture("activated-primary", Path.Combine(saves, "activated-primary.vcdbs"));
        secondary = new Fixture("activated-secondary", Path.Combine(saves, "activated-secondary.vcdbs"));
        if (File.Exists(primary.SavePath) || File.Exists(secondary.SavePath)) throw new InvalidOperationException("L00-C bootstrap refuses to overwrite a fixture save.");
        Receipt("bootstrap-open", null, "ready");
    }

    // Called only by the process-global ScreenManager pump.  It deliberately
    // receives no ICoreClientAPI and retains no per-session object.
    internal bool TryAdvance(object screenManager, out L00CMenuActionLaboratoryHost? completedHost)
    {
        RequireDebugLaboratory(); completedHost = null;
        switch (state)
        {
            case State.WaitPrimaryMenu:
                if (!StableMenu(screenManager)) return false;
                state = State.WaitPrimaryWorld; Create(screenManager, primary); return false;
            case State.WaitPrimaryWorld:
                if (!StableFinalizedNewWorld(screenManager, primary, out object? main)) return false;
                L00CMenuActionDriver.ReturnToMainMenu(main!, screenManager); Receipt("primary-created-returned", primary, "return-main-menu"); state = State.WaitPrimaryCell; return false;
            case State.WaitPrimaryCell:
                if (!TryBind(screenManager, primary)) return false;
                Publish(primary); Receipt("primary-cell-confirmed", primary, "GuiScreenSingleplayer.entries"); state = State.WaitSecondaryMenu; return false;
            case State.WaitSecondaryMenu:
                if (!StableMenu(screenManager)) return false;
                state = State.WaitSecondaryWorld; Create(screenManager, secondary); return false;
            case State.WaitSecondaryWorld:
                if (!StableFinalizedNewWorld(screenManager, secondary, out main)) return false;
                L00CMenuActionDriver.ReturnToMainMenu(main!, screenManager); Receipt("secondary-created-returned", secondary, "return-main-menu"); state = State.WaitSecondaryCell; return false;
            case State.WaitSecondaryCell:
                if (!TryBind(screenManager, secondary)) return false;
                Publish(secondary); Receipt("secondary-cell-confirmed", secondary, "GuiScreenSingleplayer.entries");
                completedHost = L00CMenuActionLaboratoryHost.Open(root, Path.Combine(evidence, "campaign"), primary.SavePath, secondary.SavePath);
                state = State.Completed; Receipt("bootstrap-complete", null, "host-open"); return true;
            case State.Completed: return true;
            default: throw new InvalidOperationException("L00-C bootstrap entered an unknown state.");
        }
    }

    private void Create(object screenManager, Fixture fixture)
    {
        fixture.LevelFinalizeObserved = false;
        L00CMenuActionReceipt receipt = L00CMenuActionDriver.CreateFixtureWorld(screenManager, fixture.Role, fixture.SavePath);
        Receipt(receipt.Action, fixture, receipt.TargetMethod);
    }
    private bool StableMenu(object screenManager)
    {
        if (!L00CMenuActionDriver.TryFindMenuLeft(screenManager, out object? menu) || menu is null) { stableTicks = 0; return false; }
        return ++stableTicks >= 3 && ResetStable();
    }
    private bool StableFinalizedNewWorld(object screenManager, Fixture fixture, out object? main)
    {
        // The process controller records IClientEventAPI.LevelFinalize. It is
        // required in addition to the native playable flags and exact local args.
        if (!L00CMenuActionDriver.TryFindFinalizedNewWorldSession(screenManager, fixture.SavePath, fixture.LevelFinalizeObserved, out main) || main is null)
        {
            stableTicks = 0;
            return false;
        }
        return ++stableTicks >= 3 && ResetStable();
    }
    private bool TryBind(object screenManager, Fixture fixture)
    {
        if (!File.Exists(fixture.SavePath)) { stableTicks = 0; return false; }
        if (!L00CMenuActionDriver.TryFindCurrentSingleplayerScreen(screenManager, out object? screen) || screen is null)
        {
            stableTicks = 0;
            if (!bindingMenuRequested && L00CMenuActionDriver.TryFindMenuLeft(screenManager, out object? menu) && menu is not null)
            {
                L00CMenuActionDriver.EnterSingleplayerMenu(menu);
                bindingMenuRequested = true;
            }
            return false;
        }
        if (++stableTicks < 3) return false;
        stableTicks = 0; bindingMenuRequested = false; fixture.CellIndex = ReadUniqueSaveCell(screen, fixture.SavePath); return true;
    }
    private bool ResetStable() { stableTicks = 0; return true; }
    // Called only by the process-wide controller's IClientEventAPI.LevelFinalize callback.
    internal void SignalLevelFinalize()
    {
        if (state == State.WaitPrimaryWorld) primary.LevelFinalizeObserved = true;
        else if (state == State.WaitSecondaryWorld) secondary.LevelFinalizeObserved = true;
    }
    private static int ReadUniqueSaveCell(object screen, string savePath)
    {
        Type type = screen.GetType();
        FieldInfo entries = type.GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("L00-C bootstrap refused: GuiScreenSingleplayer.entries drifted.");
        Array list = entries.GetValue(screen) as Array ?? throw new InvalidOperationException("L00-C bootstrap refused: GuiScreenSingleplayer.entries is absent.");
        Type entryType = entries.FieldType.GetElementType() ?? throw new InvalidOperationException("L00-C bootstrap refused: save entry shape drifted.");
        FieldInfo filename = entryType.GetField("Filename", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("L00-C bootstrap refused: SaveGameEntry.Filename drifted.");
        int found = -1;
        for (int i = 0; i < list.Length; i++)
        {
            object? entry = list.GetValue(i); if (entry is null) continue;
            if (filename.GetValue(entry) is string value && string.Equals(Path.GetFullPath(value), Path.GetFullPath(savePath), StringComparison.OrdinalIgnoreCase))
            { if (found >= 0) throw new InvalidOperationException("L00-C bootstrap refused: fixture save has multiple live menu cells."); found = i; }
        }
        if (found < 0) throw new InvalidOperationException("L00-C bootstrap refused: created save has no live GuiScreenSingleplayer cell.");
        return found;
    }
    private void Publish(Fixture fixture)
    {
        if (fixture.CellIndex < 0 || !File.Exists(fixture.SavePath)) throw new InvalidOperationException("L00-C bootstrap cannot publish an unobserved fixture.");
        string marker = fixture.SavePath + ".l00c-lab.json";
        if (File.Exists(marker)) throw new InvalidOperationException("L00-C bootstrap refuses to replace an existing cell marker.");
        string json = "{\"Schema\":\"l00c-lab-save-marker-v1\",\"WorldRole\":\"" + fixture.Role + "\",\"SavePath\":\"" + Escape(fixture.SavePath) + "\",\"LaboratoryRoot\":\"" + Escape(root) + "\",\"ClientSaveCellIndex\":" + fixture.CellIndex + ",\"ClientCellBindingConfirmed\":true,\"CreatedUtc\":\"" + DateTimeOffset.UtcNow.ToString("o") + "\"}";
        using (var markerStream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { byte[] bytes = Encoding.UTF8.GetBytes(json); markerStream.Write(bytes, 0, bytes.Length); }
        string lab = Path.Combine(root, ".isrworldgen-lab");
        if (!File.Exists(lab))
        {
            using var markerStream = new FileStream(lab, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        } // Publish only after real native evidence.
    }
    private void Receipt(string phase, Fixture? fixture, string detail)
    {
        string file = Path.Combine(evidence, string.Format("{0:D2}-{1}.json", (int)state, phase));
        string target = fixture is null ? "null" : "{\"role\":\"" + fixture.Role + "\",\"savePath\":\"" + Escape(fixture.SavePath) + "\",\"cellIndex\":" + fixture.CellIndex + "}";
        string json = "{\"schema\":\"l00c-fixture-bootstrap-v1\",\"phase\":\"" + phase + "\",\"state\":\"" + state + "\",\"target\":" + target + ",\"detail\":\"" + Escape(detail) + "\",\"utc\":\"" + DateTimeOffset.UtcNow.ToString("o") + "\"}";
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None); byte[] bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes, 0, bytes.Length);
    }
    private static string CanonicalLaboratoryRoot(string path) { string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); DirectoryInfo? parent = Directory.GetParent(full); if (parent is null || !string.Equals(Path.GetFileName(full), "L00C", StringComparison.OrdinalIgnoreCase) || !string.Equals(parent.Name, ".local", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("L00-C bootstrap requires the real repository .local\\L00C root."); return full; }
    private static bool IsUnder(string path, string parent) => path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static void RequireDebugLaboratory() { if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal)) throw new InvalidOperationException("L00-C bootstrap requires Debugger.IsAttached and ISR_L00C_LAB=1."); }
    private enum State { WaitPrimaryMenu, WaitPrimaryWorld, WaitPrimaryCell, WaitSecondaryMenu, WaitSecondaryWorld, WaitSecondaryCell, Completed }
    private sealed class Fixture { internal Fixture(string role, string savePath) { Role = role; SavePath = Path.GetFullPath(savePath); } internal string Role { get; } internal string SavePath { get; } internal int CellIndex { get; set; } = -1; internal bool LevelFinalizeObserved { get; set; } }
}
