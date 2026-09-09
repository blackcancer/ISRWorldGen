#if DEBUG || L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ISRWorldGen.L00C.Laboratory;

internal enum L00CScenarioStage
{
    WaitMenuBeforeCreateA,
    CreatingA,
    WaitCreatedAReady,
    SavingCreatedA,
    WaitMenuAfterCreatedASave,
    ReopeningA,
    WaitReopenedAReady,
    SavingReopenedA,
    WaitMenuBeforeCreateB,
    CreatingB,
    WaitCreatedBReady,
    SavingCreatedB,
    WaitMenuAfterCreatedBSave,
    IterationVerified,
    RunCompleted
}

internal enum L00CScenarioActionKind { None, Create, Reopen, SaveQuit }
internal enum L00CScenarioActionProgress { None, NotStarted, Started, NativeEffectCompleted }
internal enum L00CScenarioObservationKind { MenuReady, SessionReady, SaveCommitted }
internal enum L00CScenarioOutcome { Running, RunCompleted, Stopped, Failed }

internal sealed class L00CScenarioException : InvalidOperationException
{
    internal L00CScenarioException(string code, L00CScenarioStage? stage, string invariant)
        : this(code, stage, invariant, null)
    {
    }

    internal L00CScenarioException(string code, L00CScenarioStage? stage, string invariant, Exception? innerException)
        : base("[" + code + "] state=" + (stage?.ToString() ?? "definition") + " invariant=" + invariant, innerException)
    {
        Code = code;
        Stage = stage;
        Invariant = invariant;
    }

    internal string Code { get; }
    internal L00CScenarioStage? Stage { get; }
    internal string Invariant { get; }
}

internal sealed class L00CScenarioTarget
{
    internal L00CScenarioTarget(int iteration, char slot, string role, string canonicalSavePath)
    {
        Iteration = iteration;
        Slot = slot;
        Role = role;
        CanonicalSavePath = canonicalSavePath;
    }

    internal int Iteration { get; }
    internal char Slot { get; }
    internal string Role { get; }
    internal string CanonicalSavePath { get; }
}

internal sealed class L00CScenarioIteration
{
    internal L00CScenarioIteration(int number, L00CScenarioTarget a, L00CScenarioTarget b)
    {
        Number = number;
        A = a;
        B = b;
    }

    internal int Number { get; }
    internal L00CScenarioTarget A { get; }
    internal L00CScenarioTarget B { get; }
}

/// <summary>
/// Immutable description of the exact five-iteration T00-06 laboratory run.
/// It owns ten distinct save identities; it does not claim that the current
/// two-save cleanup manifest can yet execute this description.
/// </summary>
internal sealed class L00CScenarioDefinition
{
    internal const int RequiredIterations = 5;
    internal const int RequiredSessions = 15;
    internal const int RequiredDedicatedSaves = 10;
    private const string SavePrefix = "ISRWorldGen-L00C-";
    private readonly ReadOnlyCollection<L00CScenarioIteration> iterations;

    private L00CScenarioDefinition(string runId, string savesDirectory, long waitTimeoutTicks,
        IList<L00CScenarioIteration> values)
    {
        RunId = runId;
        SavesDirectory = savesDirectory;
        WaitTimeoutTicks = waitTimeoutTicks;
        iterations = new ReadOnlyCollection<L00CScenarioIteration>(new List<L00CScenarioIteration>(values));
    }

    internal string RunId { get; }
    internal string SavesDirectory { get; }
    internal long WaitTimeoutTicks { get; }
    internal IReadOnlyList<L00CScenarioIteration> Iterations => iterations;

    internal static L00CScenarioDefinition CreateOwned(string runId, string canonicalSavesDirectory, long waitTimeoutTicks)
    {
        RequireRunId(runId);
        string saves = RequireCanonicalDirectory(canonicalSavesDirectory, "L00C_S2_SAVES_DIRECTORY_NOT_CANONICAL");
        if (waitTimeoutTicks <= 0)
            throw new L00CScenarioException("L00C_S2_TIMEOUT_BOUND_INVALID", null, "waitTimeoutTicks must be positive");

        var values = new List<L00CScenarioIteration>(RequiredIterations);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int iteration = 1; iteration <= RequiredIterations; iteration++)
        {
            L00CScenarioTarget a = CreateTarget(runId, saves, iteration, 'A');
            L00CScenarioTarget b = CreateTarget(runId, saves, iteration, 'B');
            if (!paths.Add(a.CanonicalSavePath) || !paths.Add(b.CanonicalSavePath))
                throw new L00CScenarioException("L00C_S2_SAVE_PATH_DUPLICATE", null, "ten scenario targets must be distinct");
            values.Add(new L00CScenarioIteration(iteration, a, b));
        }
        return new L00CScenarioDefinition(runId, saves, waitTimeoutTicks, values);
    }

    internal L00CScenarioIteration GetIteration(int number)
    {
        if (number < 1 || number > RequiredIterations)
            throw new L00CScenarioException("L00C_S2_ITERATION_OUT_OF_RANGE", null, "iteration must be within exact 1..5 run");
        return iterations[number - 1];
    }

    internal static string RequireCanonicalSavePath(string path, string code)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            throw new L00CScenarioException(code, null, "save path must be an absolute canonical .vcdbs path");
        string full = Path.GetFullPath(path);
        if (!string.Equals(path, full, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(full), ".vcdbs", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(full)))
            throw new L00CScenarioException(code, null, "save path must already equal its absolute canonical .vcdbs form");
        return full;
    }

    private static L00CScenarioTarget CreateTarget(string runId, string saves, int iteration, char slot)
    {
        string slotText = char.ToLowerInvariant(slot).ToString();
        string role = "iteration-" + iteration.ToString("D2") + "-" + slotText;
        string path = RequireCanonicalSavePath(
            Path.Combine(saves, SavePrefix + runId + "-" + role + ".vcdbs"),
            "L00C_S2_GENERATED_SAVE_PATH_INVALID");
        if (!string.Equals(Path.GetDirectoryName(path), saves, StringComparison.OrdinalIgnoreCase))
            throw new L00CScenarioException("L00C_S2_SAVE_PATH_ESCAPED", null, "scenario target escaped GamePaths.Saves");
        return new L00CScenarioTarget(iteration, slot, role, path);
    }

    private static string RequireCanonicalDirectory(string path, string code)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            throw new L00CScenarioException(code, null, "directory must be absolute and canonical");
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), full, StringComparison.OrdinalIgnoreCase))
            throw new L00CScenarioException(code, null, "directory must already equal its canonical form");
        return full;
    }

    private static void RequireRunId(string runId)
    {
        if (runId is null || runId.Length != 32)
            throw new L00CScenarioException("L00C_S2_RUN_ID_INVALID", null, "runId must be 32 lower-case hexadecimal characters");
        foreach (char value in runId)
            if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')))
                throw new L00CScenarioException("L00C_S2_RUN_ID_INVALID", null, "runId must be 32 lower-case hexadecimal characters");
    }
}

/// <summary>
/// Proposed small S3 handoff shape. Ready and SaveCommitted are different
/// events: LevelFinalize may establish Ready, but can never establish a disk
/// commit. No API, world, screen, or client object is retained here.
/// </summary>
internal sealed class L00CScenarioObservation
{
    private L00CScenarioObservation(L00CScenarioObservationKind kind, int sessionOrdinal,
        string? canonicalSavePath, string? canonicalSavegameGuid, bool isNew, bool eventObserved)
    {
        Kind = kind;
        SessionOrdinal = sessionOrdinal;
        CanonicalSavePath = canonicalSavePath;
        CanonicalSavegameGuid = canonicalSavegameGuid;
        IsNew = isNew;
        EventObserved = eventObserved;
    }

    internal L00CScenarioObservationKind Kind { get; }
    internal int SessionOrdinal { get; }
    internal string? CanonicalSavePath { get; }
    internal string? CanonicalSavegameGuid { get; }
    internal bool IsNew { get; }
    internal bool EventObserved { get; }

    internal static L00CScenarioObservation MenuReady()
        => new(L00CScenarioObservationKind.MenuReady, 0, null, null, false, true);

    internal static L00CScenarioObservation SessionReady(int sessionOrdinal, string canonicalSavePath,
        string canonicalSavegameGuid, bool isNew, bool readyEventObserved = true)
        => new(L00CScenarioObservationKind.SessionReady, sessionOrdinal,
            L00CScenarioDefinition.RequireCanonicalSavePath(canonicalSavePath, "L00C_S2_OBSERVED_SAVE_PATH_NOT_CANONICAL"),
            RequireCanonicalGuid(canonicalSavegameGuid, "L00C_S2_OBSERVED_SAVE_GUID_NOT_CANONICAL"), isNew, readyEventObserved);

    internal static L00CScenarioObservation SaveCommitted(int sessionOrdinal, string canonicalSavePath,
        string canonicalSavegameGuid, bool commitEventObserved = true)
        => new(L00CScenarioObservationKind.SaveCommitted, sessionOrdinal,
            L00CScenarioDefinition.RequireCanonicalSavePath(canonicalSavePath, "L00C_S2_COMMITTED_SAVE_PATH_NOT_CANONICAL"),
            RequireCanonicalGuid(canonicalSavegameGuid, "L00C_S2_COMMITTED_SAVE_GUID_NOT_CANONICAL"), false, commitEventObserved);

    internal static string RequireCanonicalGuid(string value, string code)
    {
        if (!Guid.TryParseExact(value, "D", out Guid parsed) || !string.Equals(value, parsed.ToString("D"), StringComparison.Ordinal))
            throw new L00CScenarioException(code, null, "save GUID must be canonical lower-case D format");
        return value;
    }
}

internal sealed class L00CScenarioSession
{
    internal L00CScenarioSession(int ordinal, string canonicalSavePath, string canonicalSavegameGuid, bool isNew)
    {
        Ordinal = ordinal;
        CanonicalSavePath = canonicalSavePath;
        CanonicalSavegameGuid = canonicalSavegameGuid;
        IsNew = isNew;
    }

    internal int Ordinal { get; }
    internal string CanonicalSavePath { get; }
    internal string CanonicalSavegameGuid { get; }
    internal bool IsNew { get; }
}

internal sealed class L00CScenarioTraceEntry
{
    internal L00CScenarioTraceEntry(int sequence, long timestamp, L00CScenarioStage stage, string code, string detail)
    { Sequence = sequence; Timestamp = timestamp; Stage = stage; Code = code; Detail = detail; }
    internal int Sequence { get; }
    internal long Timestamp { get; }
    internal L00CScenarioStage Stage { get; }
    internal string Code { get; }
    internal string Detail { get; }
}

/// <summary>Immutable transition state for one exact five-iteration run.</summary>
internal sealed class L00CScenarioState
{
    private readonly ReadOnlyCollection<string> observedSavegameGuids;
    private readonly ReadOnlyCollection<L00CScenarioTraceEntry> trace;

    private L00CScenarioState(L00CScenarioDefinition definition, int iteration, L00CScenarioStage stage,
        L00CScenarioOutcome outcome, long lastTimestamp, long deadlineTimestamp,
        L00CScenarioActionProgress actionProgress, L00CScenarioSession? activeSession,
        string? createdAGuid, IList<string> observedGuids, IList<L00CScenarioTraceEntry> traceEntries,
        string? terminalCode, string? terminalInvariant)
    {
        Definition = definition;
        Iteration = iteration;
        Stage = stage;
        Outcome = outcome;
        LastTimestamp = lastTimestamp;
        DeadlineTimestamp = deadlineTimestamp;
        ActionProgress = actionProgress;
        ActiveSession = activeSession;
        CreatedAGuid = createdAGuid;
        observedSavegameGuids = new ReadOnlyCollection<string>(new List<string>(observedGuids));
        trace = new ReadOnlyCollection<L00CScenarioTraceEntry>(new List<L00CScenarioTraceEntry>(traceEntries));
        TerminalCode = terminalCode;
        TerminalInvariant = terminalInvariant;
    }

    internal L00CScenarioDefinition Definition { get; }
    internal int Iteration { get; }
    internal L00CScenarioStage Stage { get; }
    internal L00CScenarioOutcome Outcome { get; }
    internal long LastTimestamp { get; }
    internal long DeadlineTimestamp { get; }
    internal L00CScenarioActionProgress ActionProgress { get; }
    internal L00CScenarioSession? ActiveSession { get; }
    internal string? CreatedAGuid { get; }
    internal IReadOnlyList<string> ObservedSavegameGuids => observedSavegameGuids;
    internal IReadOnlyList<L00CScenarioTraceEntry> Trace => trace;
    internal string? TerminalCode { get; }
    internal string? TerminalInvariant { get; }
    internal bool IsTerminal => Outcome != L00CScenarioOutcome.Running;
    internal bool IsWaiting => IsWaitStage(Stage);
    internal L00CScenarioActionKind PendingAction => ActionFor(Stage);
    internal int ExpectedSessionOrdinal => checked((Iteration - 1) * 3 + SessionOffset(Stage));
    internal L00CScenarioTarget ExpectedTarget => TargetFor(Definition.GetIteration(Iteration), Stage);

    internal static L00CScenarioState Start(L00CScenarioDefinition definition, long timestamp)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (timestamp < 0) throw new L00CScenarioException("L00C_S2_MONOTONIC_TIME_INVALID", null, "initial timestamp must be non-negative");
        var entries = new List<L00CScenarioTraceEntry>
        {
            new(1, timestamp, L00CScenarioStage.WaitMenuBeforeCreateA, "L00C_S2_RUN_STARTED", "iterations=5;sessions=15;saves=10")
        };
        return new L00CScenarioState(definition, 1, L00CScenarioStage.WaitMenuBeforeCreateA,
            L00CScenarioOutcome.Running, timestamp, Deadline(definition, timestamp),
            L00CScenarioActionProgress.None, null, null, Array.Empty<string>(), entries, null, null);
    }

    internal L00CScenarioState Poll(long timestamp)
    {
        if (IsTerminal) return this;
        L00CScenarioState timed = ValidateTimestamp(timestamp);
        if (timed.IsTerminal || !timed.IsWaiting || timestamp <= timed.DeadlineTimestamp) return timed;
        return timed.Fail(timestamp, "L00C_S2_TIMEOUT_" + timed.Stage.ToString().ToUpperInvariant(),
            "bounded wait expired;iteration=" + timed.Iteration + ";session=" + SafeExpectedSession(timed));
    }

    internal L00CScenarioState AcceptObservation(L00CScenarioObservation observation, long timestamp)
    {
        if (observation is null) throw new ArgumentNullException(nameof(observation));
        L00CScenarioState current = Poll(timestamp);
        if (current.IsTerminal) return current;
        if (!current.IsWaiting)
            return current.Fail(timestamp, "L00C_S2_OBSERVATION_OUTSIDE_WAIT", "observation=" + observation.Kind);

        return current.Stage switch
        {
            L00CScenarioStage.WaitMenuBeforeCreateA => current.AcceptMenu(observation, timestamp, L00CScenarioStage.CreatingA, "L00C_S2_MENU_BEFORE_A_READY"),
            L00CScenarioStage.WaitCreatedAReady => current.AcceptReady(observation, timestamp, true, false, L00CScenarioStage.SavingCreatedA),
            L00CScenarioStage.WaitMenuAfterCreatedASave => current.AcceptCommit(observation, timestamp, L00CScenarioStage.ReopeningA),
            L00CScenarioStage.WaitReopenedAReady => current.AcceptReady(observation, timestamp, false, true, L00CScenarioStage.SavingReopenedA),
            L00CScenarioStage.WaitMenuBeforeCreateB => current.AcceptCommit(observation, timestamp, L00CScenarioStage.CreatingB),
            L00CScenarioStage.WaitCreatedBReady => current.AcceptReady(observation, timestamp, true, false, L00CScenarioStage.SavingCreatedB),
            L00CScenarioStage.WaitMenuAfterCreatedBSave => current.AcceptCommit(observation, timestamp, L00CScenarioStage.IterationVerified),
            _ => current.Fail(timestamp, "L00C_S2_WAIT_STATE_UNMAPPED", "wait state has no observation transition")
        };
    }

    internal L00CScenarioState BeginAction(long timestamp)
    {
        L00CScenarioState current = ValidateTimestamp(timestamp);
        if (current.IsTerminal) return current;
        if (current.PendingAction == L00CScenarioActionKind.None || current.ActionProgress != L00CScenarioActionProgress.NotStarted)
            return current.Fail(timestamp, "L00C_S2_ACTION_BEGIN_INVALID", "stage=" + current.Stage + ";progress=" + current.ActionProgress);
        return current.Change(timestamp, current.Stage, 0, L00CScenarioActionProgress.Started,
            current.ActiveSession, current.CreatedAGuid, current.observedSavegameGuids,
            "L00C_S2_" + current.PendingAction.ToString().ToUpperInvariant() + "_BEGIN", current.TargetDetail(), null, null);
    }

    internal L00CScenarioState MarkNativeEffectCompleted(long timestamp)
    {
        L00CScenarioState current = ValidateTimestamp(timestamp);
        if (current.IsTerminal) return current;
        if (current.PendingAction == L00CScenarioActionKind.None || current.ActionProgress != L00CScenarioActionProgress.Started)
            return current.Fail(timestamp, "L00C_S2_NATIVE_EFFECT_COMPLETION_INVALID", "stage=" + current.Stage + ";progress=" + current.ActionProgress);
        return current.Change(timestamp, current.Stage, 0, L00CScenarioActionProgress.NativeEffectCompleted,
            current.ActiveSession, current.CreatedAGuid, current.observedSavegameGuids,
            "L00C_S2_" + current.PendingAction.ToString().ToUpperInvariant() + "_NATIVE_EFFECT_COMPLETED", current.TargetDetail(), null, null);
    }

    internal L00CScenarioState CompleteAction(long timestamp)
    {
        L00CScenarioState current = ValidateTimestamp(timestamp);
        if (current.IsTerminal) return current;
        if (current.PendingAction == L00CScenarioActionKind.None || current.ActionProgress != L00CScenarioActionProgress.NativeEffectCompleted)
            return current.Fail(timestamp, "L00C_S2_ACTION_COMPLETION_INVALID", "stage=" + current.Stage + ";progress=" + current.ActionProgress);

        L00CScenarioStage next = current.Stage switch
        {
            L00CScenarioStage.CreatingA => L00CScenarioStage.WaitCreatedAReady,
            L00CScenarioStage.SavingCreatedA => L00CScenarioStage.WaitMenuAfterCreatedASave,
            L00CScenarioStage.ReopeningA => L00CScenarioStage.WaitReopenedAReady,
            L00CScenarioStage.SavingReopenedA => L00CScenarioStage.WaitMenuBeforeCreateB,
            L00CScenarioStage.CreatingB => L00CScenarioStage.WaitCreatedBReady,
            L00CScenarioStage.SavingCreatedB => L00CScenarioStage.WaitMenuAfterCreatedBSave,
            _ => throw new L00CScenarioException("L00C_S2_ACTION_STAGE_UNMAPPED", current.Stage, "action stage has no completion transition")
        };
        return current.Change(timestamp, next, Deadline(current.Definition, timestamp), L00CScenarioActionProgress.None,
            current.ActiveSession, current.CreatedAGuid, current.observedSavegameGuids,
            "L00C_S2_" + current.PendingAction.ToString().ToUpperInvariant() + "_COMPLETED", current.TargetDetail(), null, null);
    }

    internal L00CScenarioState AdvanceVerifiedIteration(long timestamp)
    {
        L00CScenarioState current = ValidateTimestamp(timestamp);
        if (current.IsTerminal) return current;
        if (current.Stage != L00CScenarioStage.IterationVerified)
            return current.Fail(timestamp, "L00C_S2_ITERATION_ADVANCE_INVALID", "only IterationVerified may advance");
        if (current.Iteration == L00CScenarioDefinition.RequiredIterations)
            return current.Change(timestamp, L00CScenarioStage.RunCompleted, 0, L00CScenarioActionProgress.None,
                null, null, current.observedSavegameGuids, "L00C_S2_RUN_COMPLETED_NOT_T00_06_PASS",
                "sessions=15;saves=10;finalMenu=true", L00CScenarioOutcome.RunCompleted, null);
        return current.Change(timestamp, L00CScenarioStage.WaitMenuBeforeCreateA, Deadline(current.Definition, timestamp),
            L00CScenarioActionProgress.None, null, null, current.observedSavegameGuids,
            "L00C_S2_ITERATION_ADVANCED", "completed=" + current.Iteration + ";next=" + (current.Iteration + 1), null, null,
            current.Iteration + 1);
    }

    internal L00CScenarioState Stop(long timestamp, string reasonCode)
    {
        if (IsTerminal) return this;
        L00CScenarioState current = ValidateTimestamp(timestamp);
        if (current.IsTerminal) return current;
        if (!IsDiagnosticCode(reasonCode))
            return current.Fail(timestamp, "L00C_S2_STOP_CODE_INVALID", "stop reason must be a bounded L00C_S2_ uppercase diagnostic token");
        string code = reasonCode + "_AT_" + current.Stage.ToString().ToUpperInvariant() + "_" + current.ActionProgress.ToString().ToUpperInvariant();
        return current.Change(timestamp, current.Stage, current.DeadlineTimestamp, current.ActionProgress,
            current.ActiveSession, current.CreatedAGuid, current.observedSavegameGuids,
            code, current.TargetDetail(), L00CScenarioOutcome.Stopped, "deterministic partial stop");
    }

    internal L00CScenarioState FailExternal(long timestamp, string code, string invariant)
    {
        if (Outcome is L00CScenarioOutcome.Failed or L00CScenarioOutcome.Stopped) return this;
        if (Outcome == L00CScenarioOutcome.RunCompleted)
        {
            if (timestamp < LastTimestamp)
                return Fail(LastTimestamp, "L00C_S2_MONOTONIC_TIME_REGRESSION", "last=" + LastTimestamp + ";actual=" + timestamp);
            if (!IsDiagnosticCode(code))
                return Fail(timestamp, "L00C_S2_EXTERNAL_CODE_INVALID", "external failure code must be a bounded L00C_S2_ uppercase diagnostic token");
            if (string.IsNullOrWhiteSpace(invariant))
                return Fail(timestamp, "L00C_S2_EXTERNAL_INVARIANT_INVALID", "external failure invariant must be non-empty");
            return Fail(timestamp, code, invariant);
        }
        L00CScenarioState current = ValidateTimestamp(timestamp);
        if (current.IsTerminal) return current;
        if (!IsDiagnosticCode(code))
            return current.Fail(timestamp, "L00C_S2_EXTERNAL_CODE_INVALID", "external failure code must be a bounded L00C_S2_ uppercase diagnostic token");
        if (string.IsNullOrWhiteSpace(invariant))
            return current.Fail(timestamp, "L00C_S2_EXTERNAL_INVARIANT_INVALID", "external failure invariant must be non-empty");
        return current.Fail(timestamp, code, invariant);
    }

    internal L00CScenarioException CreateTerminalException()
    {
        if (!IsTerminal || Outcome == L00CScenarioOutcome.RunCompleted)
            throw new L00CScenarioException("L00C_S2_TERMINAL_EXCEPTION_INVALID", Stage, "only failed/stopped state has a terminal exception");
        return new L00CScenarioException(TerminalCode!, Stage, TerminalInvariant!);
    }

    private L00CScenarioState AcceptMenu(L00CScenarioObservation observation, long timestamp,
        L00CScenarioStage next, string code)
    {
        if (observation.Kind != L00CScenarioObservationKind.MenuReady || !observation.EventObserved)
            return Fail(timestamp, "L00C_S2_MENU_OBSERVATION_INVALID", "expected MenuReady event");
        return Change(timestamp, next, 0, L00CScenarioActionProgress.NotStarted, null, CreatedAGuid,
            observedSavegameGuids, code, "iteration=" + Iteration, null, null);
    }

    private L00CScenarioState AcceptReady(L00CScenarioObservation observation, long timestamp,
        bool expectedIsNew, bool requireCreatedAGuid, L00CScenarioStage next)
    {
        if (observation.Kind != L00CScenarioObservationKind.SessionReady || !observation.EventObserved)
            return observation.Kind == L00CScenarioObservationKind.SessionReady
                ? this
                : Fail(timestamp, "L00C_S2_READY_OBSERVATION_INVALID", "expected SessionReady event");
        L00CScenarioTarget target = ExpectedTarget;
        if (observation.SessionOrdinal != ExpectedSessionOrdinal)
            return Fail(timestamp, "L00C_S2_READY_SESSION_ORDINAL_MISMATCH", "expected=" + ExpectedSessionOrdinal + ";actual=" + observation.SessionOrdinal);
        if (!SamePath(observation.CanonicalSavePath!, target.CanonicalSavePath))
            return Fail(timestamp, "L00C_S2_READY_SAVE_PATH_MISMATCH", "expected=" + target.CanonicalSavePath + ";actual=" + observation.CanonicalSavePath);
        if (observation.IsNew != expectedIsNew)
            return Fail(timestamp, "L00C_S2_READY_ISNEW_MISMATCH", "expected=" + expectedIsNew + ";actual=" + observation.IsNew);

        string guid = observation.CanonicalSavegameGuid!;
        if (requireCreatedAGuid && !string.Equals(guid, CreatedAGuid, StringComparison.Ordinal))
            return Fail(timestamp, "L00C_S2_REOPEN_SAVE_GUID_MISMATCH", "reopened A must retain the created A GUID");
        if (!requireCreatedAGuid && ContainsGuid(observedSavegameGuids, guid))
            return Fail(timestamp, "L00C_S2_NEW_SAVE_GUID_REUSED", "new A/B save GUID must be distinct");

        var guids = new List<string>(observedSavegameGuids);
        if (!requireCreatedAGuid) guids.Add(guid);
        string? aGuid = Stage == L00CScenarioStage.WaitCreatedAReady ? guid : CreatedAGuid;
        var session = new L00CScenarioSession(observation.SessionOrdinal, target.CanonicalSavePath, guid, expectedIsNew);
        return Change(timestamp, next, 0, L00CScenarioActionProgress.NotStarted, session, aGuid, guids,
            "L00C_S2_SESSION_READY_EXACT", "session=" + observation.SessionOrdinal + ";target=" + target.Role + ";isNew=" + expectedIsNew, null, null);
    }

    private L00CScenarioState AcceptCommit(L00CScenarioObservation observation, long timestamp, L00CScenarioStage next)
    {
        if (observation.Kind != L00CScenarioObservationKind.SaveCommitted || !observation.EventObserved)
            return observation.Kind == L00CScenarioObservationKind.SaveCommitted
                ? this
                : Fail(timestamp, "L00C_S2_COMMIT_OBSERVATION_INVALID", "expected SaveCommitted event independent of LevelFinalize");
        if (ActiveSession is null)
            return Fail(timestamp, "L00C_S2_COMMIT_WITHOUT_SESSION", "save commit must bind the just-closed session");
        if (observation.SessionOrdinal != ActiveSession.Ordinal)
            return Fail(timestamp, "L00C_S2_COMMIT_SESSION_ORDINAL_MISMATCH", "expected=" + ActiveSession.Ordinal + ";actual=" + observation.SessionOrdinal);
        if (!SamePath(observation.CanonicalSavePath!, ActiveSession.CanonicalSavePath))
            return Fail(timestamp, "L00C_S2_COMMIT_SAVE_PATH_MISMATCH", "commit path differs from closed session");
        if (!string.Equals(observation.CanonicalSavegameGuid, ActiveSession.CanonicalSavegameGuid, StringComparison.Ordinal))
            return Fail(timestamp, "L00C_S2_COMMIT_SAVE_GUID_MISMATCH", "commit GUID differs from closed session");

        string code = next == L00CScenarioStage.IterationVerified ? "L00C_S2_ITERATION_VERIFIED" : "L00C_S2_SAVE_COMMITTED_EXACT";
        L00CScenarioActionProgress progress = next == L00CScenarioStage.IterationVerified
            ? L00CScenarioActionProgress.None : L00CScenarioActionProgress.NotStarted;
        return Change(timestamp, next, 0, progress, null, CreatedAGuid, observedSavegameGuids,
            code, "session=" + observation.SessionOrdinal + ";target=" + ExpectedTarget.Role, null, null);
    }

    private L00CScenarioState ValidateTimestamp(long timestamp)
    {
        if (IsTerminal) return this;
        if (timestamp < LastTimestamp)
            return Fail(LastTimestamp, "L00C_S2_MONOTONIC_TIME_REGRESSION", "last=" + LastTimestamp + ";actual=" + timestamp);
        if (timestamp == LastTimestamp) return this;
        return new L00CScenarioState(Definition, Iteration, Stage, Outcome, timestamp, DeadlineTimestamp,
            ActionProgress, ActiveSession, CreatedAGuid, observedSavegameGuids, trace, TerminalCode, TerminalInvariant);
    }

    private L00CScenarioState Fail(long timestamp, string code, string invariant)
        => Change(timestamp, Stage, DeadlineTimestamp, ActionProgress, ActiveSession, CreatedAGuid,
            observedSavegameGuids, code, invariant, L00CScenarioOutcome.Failed, invariant);

    private L00CScenarioState Change(long timestamp, L00CScenarioStage nextStage, long deadline,
        L00CScenarioActionProgress progress, L00CScenarioSession? session, string? createdGuid,
        IList<string> guids, string traceCode, string traceDetail, L00CScenarioOutcome? outcome,
        string? terminalInvariant, int? iterationOverride = null)
    {
        var entries = new List<L00CScenarioTraceEntry>(trace)
        {
            new(trace.Count + 1, timestamp, nextStage, traceCode, traceDetail)
        };
        L00CScenarioOutcome nextOutcome = outcome ?? L00CScenarioOutcome.Running;
        return new L00CScenarioState(Definition, iterationOverride ?? Iteration, nextStage, nextOutcome,
            timestamp, deadline, progress, session, createdGuid, guids, entries,
            nextOutcome is L00CScenarioOutcome.Failed or L00CScenarioOutcome.Stopped ? traceCode : null,
            nextOutcome is L00CScenarioOutcome.Failed or L00CScenarioOutcome.Stopped ? terminalInvariant : null);
    }

    private string TargetDetail()
    {
        if (Stage == L00CScenarioStage.RunCompleted) return "run-completed";
        return "iteration=" + Iteration + ";session=" + ExpectedSessionOrdinal + ";target=" + ExpectedTarget.Role;
    }

    private static long Deadline(L00CScenarioDefinition definition, long timestamp)
    {
        try { return checked(timestamp + definition.WaitTimeoutTicks); }
        catch (OverflowException) { throw new L00CScenarioException("L00C_S2_TIMEOUT_BOUND_OVERFLOW", null, "monotonic deadline overflow"); }
    }

    private static bool IsWaitStage(L00CScenarioStage stage) => stage is
        L00CScenarioStage.WaitMenuBeforeCreateA or L00CScenarioStage.WaitCreatedAReady or
        L00CScenarioStage.WaitMenuAfterCreatedASave or L00CScenarioStage.WaitReopenedAReady or
        L00CScenarioStage.WaitMenuBeforeCreateB or L00CScenarioStage.WaitCreatedBReady or
        L00CScenarioStage.WaitMenuAfterCreatedBSave;

    private static L00CScenarioActionKind ActionFor(L00CScenarioStage stage) => stage switch
    {
        L00CScenarioStage.CreatingA or L00CScenarioStage.CreatingB => L00CScenarioActionKind.Create,
        L00CScenarioStage.ReopeningA => L00CScenarioActionKind.Reopen,
        L00CScenarioStage.SavingCreatedA or L00CScenarioStage.SavingReopenedA or L00CScenarioStage.SavingCreatedB => L00CScenarioActionKind.SaveQuit,
        _ => L00CScenarioActionKind.None
    };

    private static int SessionOffset(L00CScenarioStage stage) => stage switch
    {
        L00CScenarioStage.WaitMenuBeforeCreateA or L00CScenarioStage.CreatingA or
        L00CScenarioStage.WaitCreatedAReady or L00CScenarioStage.SavingCreatedA or
        L00CScenarioStage.WaitMenuAfterCreatedASave => 1,
        L00CScenarioStage.ReopeningA or L00CScenarioStage.WaitReopenedAReady or
        L00CScenarioStage.SavingReopenedA or L00CScenarioStage.WaitMenuBeforeCreateB => 2,
        L00CScenarioStage.CreatingB or L00CScenarioStage.WaitCreatedBReady or
        L00CScenarioStage.SavingCreatedB or L00CScenarioStage.WaitMenuAfterCreatedBSave or
        L00CScenarioStage.IterationVerified => 3,
        _ => 3
    };

    private static L00CScenarioTarget TargetFor(L00CScenarioIteration iteration, L00CScenarioStage stage)
        => SessionOffset(stage) == 3 ? iteration.B : iteration.A;

    private static int SafeExpectedSession(L00CScenarioState state)
        => state.Stage == L00CScenarioStage.RunCompleted ? 0 : state.ExpectedSessionOrdinal;

    private static bool SamePath(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsGuid(IList<string> values, string guid)
    {
        foreach (string value in values)
            if (string.Equals(value, guid, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool IsDiagnosticCode(string? code)
    {
        if (code is null || code.Length < 9 || code.Length > 128 ||
            !code.StartsWith("L00C_S2_", StringComparison.Ordinal)) return false;
        foreach (char value in code)
            if (!((value >= 'A' && value <= 'Z') || (value >= '0' && value <= '9') || value == '_')) return false;
        return true;
    }
}
#endif
