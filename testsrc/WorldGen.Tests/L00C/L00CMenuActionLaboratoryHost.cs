// Debug-only host; it neither discovers UI nor launches/authenticates a client.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Globalization;

namespace ISRWorldGen.L00C.Laboratory;

public sealed class L00CMenuActionLaboratoryHost
{
    private const int RequiredPrimaryCycles = 5;
    private readonly string evidenceDirectory;
    private readonly L00CMarkedSaveCell primary;
    private readonly L00CMarkedSaveCell secondary;
    private readonly List<string> chronology = new();
    private State state = State.ExpectPrimaryMenu;
    private int primaryCycles;
    private int stableTicks;

    private L00CMenuActionLaboratoryHost(string evidenceDirectory, L00CMarkedSaveCell primary, L00CMarkedSaveCell secondary)
    { this.evidenceDirectory = evidenceDirectory; this.primary = primary; this.secondary = secondary; }

    public static L00CMenuActionLaboratoryHost Open(string laboratoryRoot, string evidenceDirectory, string primarySave, string secondarySave)
    {
        RequireDebugLaboratory();
        L00CMarkedSaveCell primary = RequireMarkedSave(laboratoryRoot, primarySave, "activated-primary");
        L00CMarkedSaveCell secondary = RequireMarkedSave(laboratoryRoot, secondarySave, "activated-secondary");
        if (string.Equals(primary.SavePath, secondary.SavePath, StringComparison.OrdinalIgnoreCase) || primary.CellIndex == secondary.CellIndex)
            throw new InvalidOperationException("L00-C requires distinct marked saves and distinct confirmed menu cells.");
        string root = CanonicalLaboratoryRoot(laboratoryRoot);
        string evidence = Path.GetFullPath(evidenceDirectory);
        if (!IsUnder(evidence, root) || Directory.Exists(evidence) || File.Exists(evidence))
            throw new InvalidOperationException("L00-C evidence must be a new directory under the marked laboratory root.");
        Directory.CreateDirectory(evidence);
        var host = new L00CMenuActionLaboratoryHost(evidence, primary, secondary);
        host.Record("host-open", null, "ready");
        host.WriteReceipt("opened");
        return host;
    }

    public void EnterSingleplayerMenu(object mainMenuLeft)
    {
        RequireDebugLaboratory();
        if (state != State.ExpectPrimaryMenu && state != State.ExpectSecondaryMenu) throw new InvalidOperationException("L00-C menu entry is outside the expected transition.");
        L00CMenuActionReceipt receipt = L00CMenuActionDriver.EnterSingleplayerMenu(mainMenuLeft);
        state = state == State.ExpectPrimaryMenu ? State.PrimaryMenuOpen : State.SecondaryMenuOpen;
        Record(receipt.Action, null, receipt.TargetMethod); WriteReceipt("entered-singleplayer");
    }

    // No index is accepted from a caller: only the marker-confirmed cell may be invoked.
    public void OpenPrimary(object singleplayerScreen)
    {
        RequireDebugLaboratory();
        if (state != State.PrimaryMenuOpen || primaryCycles >= RequiredPrimaryCycles) throw new InvalidOperationException("L00-C primary open is outside the expected transition.");
        L00CMenuActionReceipt receipt = L00CMenuActionDriver.ReopenPrimaryWorld(singleplayerScreen, primary.CellIndex);
        primaryCycles++; state = State.PrimaryWorldOpen;
        Record(receipt.Action, primary, receipt.TargetMethod); WriteReceipt("primary-open-" + primaryCycles);
    }

    public void OpenSecondary(object singleplayerScreen)
    {
        RequireDebugLaboratory();
        if (state != State.SecondaryMenuOpen || primaryCycles != RequiredPrimaryCycles) throw new InvalidOperationException("L00-C secondary open is outside the expected transition.");
        L00CMenuActionReceipt receipt = L00CMenuActionDriver.ReopenPrimaryWorld(singleplayerScreen, secondary.CellIndex);
        state = State.SecondaryWorldOpen;
        Record("open-secondary-world", secondary, receipt.TargetMethod); WriteReceipt("secondary-open");
    }

    public void ReturnToMainMenu(object clientMain, object screenManager)
    {
        RequireDebugLaboratory();
        L00CMarkedSaveCell target;
        if (state == State.PrimaryWorldOpen) target = primary;
        else if (state == State.SecondaryWorldOpen) target = secondary;
        else throw new InvalidOperationException("L00-C return is outside the expected transition.");
        L00CMenuActionReceipt receipt = L00CMenuActionDriver.ReturnToMainMenu(clientMain, screenManager);
        state = target == primary ? (primaryCycles == RequiredPrimaryCycles ? State.ExpectSecondaryMenu : State.ExpectPrimaryMenu) : State.ReadyToComplete;
        Record(receipt.Action, target, receipt.TargetMethod); WriteReceipt("returned-main-menu");
    }

    public void Complete()
    {
        RequireDebugLaboratory();
        if (state != State.ReadyToComplete || primaryCycles != RequiredPrimaryCycles) throw new InvalidOperationException("L00-C requires five primary returns then the secondary final return.");
        state = State.Completed; Record("campaign-complete", null, "none"); WriteReceipt("complete");
    }

    /// <summary>
    /// Advances from the process-global ScreenManager pump.  It retains no client API,
    /// world, session or ClientMain after an action returns.
    /// </summary>
    public bool TryAdvance(object screenManager)
    {
        RequireDebugLaboratory();
        if (screenManager is null) throw new ArgumentNullException(nameof(screenManager));

        switch (state)
        {
            case State.ExpectPrimaryMenu:
            case State.ExpectSecondaryMenu:
                if (!L00CMenuActionDriver.TryFindMenuLeft(screenManager, out object? menuLeft) || menuLeft is null) { stableTicks = 0; return false; }
                if (++stableTicks < 3) return false;
                stableTicks = 0; EnterSingleplayerMenu(menuLeft); return false;
            case State.PrimaryMenuOpen:
            case State.SecondaryMenuOpen:
                if (!L00CMenuActionDriver.TryFindCurrentSingleplayerScreen(screenManager, out object? singleplayer) || singleplayer is null) { stableTicks = 0; return false; }
                if (++stableTicks < 3) return false;
                stableTicks = 0;
                if (state == State.PrimaryMenuOpen) OpenPrimary(singleplayer); else OpenSecondary(singleplayer);
                return false;
            case State.PrimaryWorldOpen:
            case State.SecondaryWorldOpen:
                // This callback is public API lifecycle evidence that the client is ticking.
                // Three ticks prevent a same-frame menu mutation after ConnectToSingleplayer.
                if (++stableTicks < 3 || !L00CMenuActionDriver.TryFindClientSession(screenManager, out object? clientMain, out _) || clientMain is null) return false;
                stableTicks = 0; ReturnToMainMenu(clientMain, screenManager); return false;
            case State.ReadyToComplete:
                Complete(); return true;
            case State.Completed:
                return true;
            default:
                throw new InvalidOperationException("L00-C laboratory host reached an unknown state.");
        }
    }

    private void Record(string action, L00CMarkedSaveCell? target, string method)
    {
        string targetText = target is null ? "none" : target.Role + "|" + target.SavePath + "|cell=" + target.CellIndex + "|confirmed=true";
        chronology.Add(DateTimeOffset.UtcNow.ToString("o") + "|" + action + "|" + targetText + "|" + method);
    }

    private void WriteReceipt(string phase)
    {
        string receipt = Path.Combine(evidenceDirectory, string.Format("{0:D2}-{1}.json", chronology.Count, phase));
        string json = "{\"schema\":\"l00c-menu-action-lab-v2\",\"phase\":\"" + Escape(phase) + "\",\"state\":\"" + state + "\",\"strictWorkflow\":\"five-primary-menu-open-return;secondary-menu-open-final-return\",\"finalSecondaryReturnRequired\":true,\"primaryCycles\":" + primaryCycles + ",\"primaryCellIndex\":" + primary.CellIndex + ",\"secondaryCellIndex\":" + secondary.CellIndex + ",\"driverVersion\":\"" + L00CMenuActionDriver.RequiredLibVersion + "\",\"driverVintagestoryLibSha256\":\"" + L00CMenuActionDriver.RequiredLibSha256 + "\",\"chronology\":[" + string.Join(",", chronology.ConvertAll(x => "\"" + Escape(x) + "\"")) + "]}";
        using var stream = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes, 0, bytes.Length);
    }

    private static L00CMarkedSaveCell RequireMarkedSave(string laboratoryRoot, string save, string expectedRole)
    {
        string root = CanonicalLaboratoryRoot(laboratoryRoot);
        string savePath = Path.GetFullPath(save);
        if (!File.Exists(savePath) || !Directory.Exists(root) || !IsUnder(savePath, Path.Combine(root, "saves")) || !savePath.EndsWith(".vcdbs", StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(root, ".isrworldgen-lab"))) throw new InvalidOperationException("L00-C refuses an unmarked or out-of-laboratory save.");
        string marker = savePath + ".l00c-lab.json";
        if (!File.Exists(marker)) throw new InvalidOperationException("L00-C save marker is absent.");
        L00CStrictJsonObject json = L00CStrictJsonObject.Parse(File.ReadAllText(marker));
        json.RequireExactly("Schema", "WorldRole", "SavePath", "LaboratoryRoot", "ClientSaveCellIndex", "ClientCellBindingConfirmed", "CreatedUtc");
        string markerRoot = Path.GetFullPath(json.RequiredString("LaboratoryRoot"));
        string markerSave = Path.GetFullPath(json.RequiredString("SavePath"));
        string role = json.RequiredString("WorldRole");
        int cell = json.RequiredNonNegativeInt("ClientSaveCellIndex");
        if (!string.Equals(json.RequiredString("Schema"), "l00c-lab-save-marker-v1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(json.RequiredString("CreatedUtc")) || !string.Equals(markerRoot, root, StringComparison.OrdinalIgnoreCase) || !string.Equals(markerSave, savePath, StringComparison.OrdinalIgnoreCase) || !string.Equals(role, expectedRole, StringComparison.Ordinal) || !json.RequiredTrue("ClientCellBindingConfirmed")) throw new InvalidOperationException("L00-C marker does not canonically bind this laboratory save and confirmed client cell.");
        return new L00CMarkedSaveCell(role, savePath, cell);
    }

    private static string CanonicalLaboratoryRoot(string path)
    {
        string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DirectoryInfo? parent = Directory.GetParent(root);
        if (parent is null || !string.Equals(Path.GetFileName(root), "L00C", StringComparison.OrdinalIgnoreCase) || !string.Equals(parent.Name, ".local", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("L00-C requires the real repository .local\\L00C root.");
        return root;
    }

    private static bool IsUnder(string path, string root) => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void RequireDebugLaboratory()
    {
#if !DEBUG
        throw new InvalidOperationException("L00-C laboratory host is disabled outside a Debug build.");
#else
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal)) throw new InvalidOperationException("L00-C laboratory host requires an attached debugger and ISR_L00C_LAB=1.");
#endif
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private enum State { ExpectPrimaryMenu, PrimaryMenuOpen, PrimaryWorldOpen, ExpectSecondaryMenu, SecondaryMenuOpen, SecondaryWorldOpen, ReadyToComplete, Completed }
    private sealed class L00CMarkedSaveCell { internal L00CMarkedSaveCell(string role, string savePath, int cellIndex) { Role = role; SavePath = savePath; CellIndex = cellIndex; } internal string Role { get; } internal string SavePath { get; } internal int CellIndex { get; } }

    // Small strict object parser: no external JSON package, duplicate keys, trailing data,
    // fractions and scientific notation are refused before marker values are consumed.
    private sealed class L00CStrictJsonObject
    {
        private readonly Dictionary<string, Value> values = new(StringComparer.Ordinal);
        private L00CStrictJsonObject() { }
        internal static L00CStrictJsonObject Parse(string text)
        {
            var result = new L00CStrictJsonObject(); int index = 0; Skip(text, ref index); Expect(text, ref index, '{'); Skip(text, ref index);
            if (Take(text, ref index, '}')) { End(text, index); return result; }
            while (true)
            {
                string name = String(text, ref index); Skip(text, ref index); Expect(text, ref index, ':'); Skip(text, ref index);
                if (result.values.ContainsKey(name)) throw new InvalidOperationException("L00-C marker contains duplicate property " + name + ".");
                result.values.Add(name, ValueOf(text, ref index));
                Skip(text, ref index); if (Take(text, ref index, '}')) break; Expect(text, ref index, ','); Skip(text, ref index);
            }
            End(text, index); return result;
        }
        internal void RequireExactly(params string[] names)
        {
            if (values.Count != names.Length) throw new InvalidOperationException("L00-C marker has missing or unexpected properties.");
            foreach (string name in names) if (!values.ContainsKey(name)) throw new InvalidOperationException("L00-C marker is missing property " + name + ".");
        }
        internal string RequiredString(string name) => Required(name, Kind.String).Text;
        internal bool RequiredTrue(string name) => Required(name, Kind.Boolean).Text == "true";
        internal int RequiredNonNegativeInt(string name)
        {
            string text = Required(name, Kind.Number).Text;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int result) || result < 0) throw new InvalidOperationException("L00-C marker has invalid non-negative integer " + name + ".");
            return result;
        }
        private Value Required(string name, Kind type)
        {
            if (!values.TryGetValue(name, out Value value) || value.Type != type) throw new InvalidOperationException("L00-C marker has invalid type for " + name + ".");
            return value;
        }
        private static Value ValueOf(string text, ref int index)
        {
            if (index >= text.Length) throw new InvalidOperationException("L00-C marker is truncated.");
            if (text[index] == '"') return new Value(Kind.String, String(text, ref index));
            if (Word(text, ref index, "true")) return new Value(Kind.Boolean, "true");
            if (Word(text, ref index, "false")) return new Value(Kind.Boolean, "false");
            int start = index; if (text[index] == '-') index++;
            if (index >= text.Length || text[index] < '0' || text[index] > '9') throw new InvalidOperationException("L00-C marker has unsupported value.");
            if (text[index] == '0') index++; else while (index < text.Length && text[index] >= '0' && text[index] <= '9') index++;
            if (index < text.Length && (text[index] == '.' || text[index] == 'e' || text[index] == 'E')) throw new InvalidOperationException("L00-C marker numbers must be strict integers.");
            return new Value(Kind.Number, text.Substring(start, index - start));
        }
        private static string String(string text, ref int index)
        {
            Expect(text, ref index, '"'); var result = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++]; if (c == '"') return result.ToString(); if (c < 0x20) throw new InvalidOperationException("L00-C marker string contains control data.");
                if (c != '\\') { result.Append(c); continue; } if (index >= text.Length) throw new InvalidOperationException("L00-C marker string is truncated.");
                switch (text[index++])
                {
                    case '"': result.Append('"'); break; case '\\': result.Append('\\'); break; case '/': result.Append('/'); break; case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break; case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                    case 'u': if (index + 4 > text.Length || !ushort.TryParse(text.Substring(index, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) throw new InvalidOperationException("L00-C marker has invalid unicode escape."); result.Append((char)code); index += 4; break;
                    default: throw new InvalidOperationException("L00-C marker has invalid string escape.");
                }
            }
            throw new InvalidOperationException("L00-C marker string is truncated.");
        }
        private static void Skip(string text, ref int index)
        {
            while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n')) index++;
        }
        private static void Expect(string text, ref int index, char value) { if (index >= text.Length || text[index++] != value) throw new InvalidOperationException("L00-C marker JSON is malformed."); }
        private static bool Take(string text, ref int index, char value) { if (index < text.Length && text[index] == value) { index++; return true; } return false; }
        private static bool Word(string text, ref int index, string value) { if (index + value.Length > text.Length || !string.Equals(text.Substring(index, value.Length), value, StringComparison.Ordinal)) return false; index += value.Length; return true; }
        private static void End(string text, int index) { Skip(text, ref index); if (index != text.Length) throw new InvalidOperationException("L00-C marker has trailing data."); }
        private enum Kind { String, Boolean, Number }
        private readonly struct Value { internal Value(Kind type, string text) { Type = type; Text = text; } internal Kind Type { get; } internal string Text { get; } }
    }
}
