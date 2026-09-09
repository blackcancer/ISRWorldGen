// Debug-only host for the immutable L00-C S2 scenario. External state changes
// are delegated through a narrow port so the ten-save manifest and S3
// observation owner remain explicit integration contracts.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

internal abstract class L00CScenarioHostAdapter
{
    internal abstract long ReadMonotonicTimestamp();
    internal abstract string? ReadStopCode();
    internal abstract bool TryObserve(object screenManager, L00CScenarioState state,
        int readyEventSessionOrdinal, out L00CScenarioObservation? observation);
    internal abstract void ExecuteNativeAction(object screenManager, L00CScenarioState state);
    internal abstract void CompleteRun(L00CScenarioState state);
}

/// <summary>
/// Native S2 implementation. The injected callbacks are the only integration
/// seams for storage/open/return ownership; all game-object access remains a
/// transient driver local and observations are immutable values.
/// </summary>
internal sealed class L00CNativeScenarioHostAdapter : L00CScenarioHostAdapter
{
    private readonly Func<string?> readStopCode;
    private readonly Action<L00CScenarioState> beginNativeOpen;
    private readonly Action<L00CScenarioState> beginNativeReturn;
    private readonly Action<L00CScenarioState> completeRun;

    internal L00CNativeScenarioHostAdapter(Func<string?> stopCode,
        Action<L00CScenarioState> nativeOpenBoundary,
        Action<L00CScenarioState> nativeReturnBoundary,
        Action<L00CScenarioState> completionBoundary)
    {
        readStopCode = stopCode ?? throw new ArgumentNullException(nameof(stopCode));
        beginNativeOpen = nativeOpenBoundary ?? throw new ArgumentNullException(nameof(nativeOpenBoundary));
        beginNativeReturn = nativeReturnBoundary ?? throw new ArgumentNullException(nameof(nativeReturnBoundary));
        completeRun = completionBoundary ?? throw new ArgumentNullException(nameof(completionBoundary));
    }

    internal override long ReadMonotonicTimestamp() => Stopwatch.GetTimestamp();
    internal override string? ReadStopCode() => readStopCode();

    internal override bool TryObserve(object screenManager, L00CScenarioState state,
        int readyEventSessionOrdinal, out L00CScenarioObservation? observation)
    {
        observation = null;
        switch (state.Stage)
        {
            case L00CScenarioStage.WaitMenuBeforeCreateA:
                if (!L00CMenuActionDriver.TryObserveMenuReady(screenManager)) return false;
                observation = L00CScenarioObservation.MenuReady();
                return true;
            case L00CScenarioStage.WaitCreatedAReady:
            case L00CScenarioStage.WaitReopenedAReady:
            case L00CScenarioStage.WaitCreatedBReady:
                bool isNew = state.Stage != L00CScenarioStage.WaitReopenedAReady;
                return L00CMenuActionDriver.TryObserveReadySession(screenManager,
                    state.ExpectedSessionOrdinal, state.ExpectedTarget, isNew,
                    readyEventSessionOrdinal, out observation);
            case L00CScenarioStage.WaitMenuAfterCreatedASave:
            case L00CScenarioStage.WaitMenuBeforeCreateB:
            case L00CScenarioStage.WaitMenuAfterCreatedBSave:
                if (state.ActiveSession is null)
                    throw new L00CScenarioException("L00C_S2_COMMIT_SESSION_STATE_ABSENT", state.Stage,
                        "save/menu observation requires the immutable just-closed session");
                return L00CMenuActionDriver.TryObserveSaveCommitted(screenManager, state.ActiveSession, out observation);
            default:
                throw new L00CScenarioException("L00C_S2_NATIVE_OBSERVATION_STAGE_INVALID", state.Stage,
                    "native adapter received a non-wait stage");
        }
    }

    internal override void ExecuteNativeAction(object screenManager, L00CScenarioState state)
    {
        if (state.ActionProgress != L00CScenarioActionProgress.Started)
            throw new L00CScenarioException("L00C_S2_NATIVE_ACTION_BOUNDARY_INVALID", state.Stage,
                "native adapter requires Started progress");
        switch (state.PendingAction)
        {
            case L00CScenarioActionKind.Create:
                _ = L00CMenuActionDriver.CreateWorld(screenManager, state.ExpectedTarget,
                    () => beginNativeOpen(state));
                return;
            case L00CScenarioActionKind.Reopen:
                _ = L00CMenuActionDriver.ReopenWorld(screenManager, state.ExpectedTarget,
                    () => beginNativeOpen(state));
                return;
            case L00CScenarioActionKind.SaveQuit:
                if (state.ActiveSession is null)
                    throw new L00CScenarioException("L00C_S2_SAVEQUIT_SESSION_STATE_ABSENT", state.Stage,
                        "SaveQuit requires the immutable ready session");
                _ = L00CMenuActionDriver.SaveAndQuit(screenManager, state.ActiveSession,
                    () => beginNativeReturn(state));
                return;
            default:
                throw new L00CScenarioException("L00C_S2_NATIVE_ACTION_KIND_INVALID", state.Stage,
                    "native adapter received action None");
        }
    }

    internal override void CompleteRun(L00CScenarioState state)
    {
        if (state.Outcome != L00CScenarioOutcome.RunCompleted || state.Stage != L00CScenarioStage.RunCompleted)
            throw new L00CScenarioException("L00C_S2_COMPLETION_BOUNDARY_INVALID", state.Stage,
                "completion callback requires RUN_COMPLETED");
        completeRun(state);
    }
}

/// <summary>
/// Drives one exact five-iteration scenario and retains only immutable state,
/// a monotonic ready-event ordinal, and append-only progress evidence.
/// </summary>
public sealed class L00CMenuActionLaboratoryHost
{
    private readonly string evidenceDirectory;
    private readonly L00CScenarioHostAdapter adapter;
    private L00CScenarioState state;
    private int readyEventSessionOrdinal;
    private int writtenTraceEntries;
    private bool completionPublished;

    private L00CMenuActionLaboratoryHost(L00CScenarioDefinition definition, string evidence,
        L00CScenarioHostAdapter scenarioAdapter)
    {
        evidenceDirectory = evidence;
        adapter = scenarioAdapter;
        state = L00CScenarioState.Start(definition, adapter.ReadMonotonicTimestamp());
        WriteNewTraceEntries();
    }

    internal static L00CMenuActionLaboratoryHost OpenIntegrated(L00CScenarioDefinition definition,
        string evidenceDirectory, L00CScenarioHostAdapter adapter)
    {
        RequireDebugLaboratory();
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (adapter is null) throw new ArgumentNullException(nameof(adapter));
        string evidence = RequireNewEvidenceDirectory(evidenceDirectory);
        Directory.CreateDirectory(evidence);
        return new L00CMenuActionLaboratoryHost(definition, evidence, adapter);
    }

    /// <summary>
    /// Advances at most one observation or one native action per main-thread
    /// pump. A terminal stop/failure always throws its exact diagnostic code;
    /// RunCompleted returns true without claiming T00-06 PASS.
    /// </summary>
    public bool TryAdvance(object screenManager)
    {
        RequireDebugLaboratory();
        if (screenManager is null) throw new ArgumentNullException(nameof(screenManager));
        if (state.IsTerminal && state.Outcome != L00CScenarioOutcome.RunCompleted)
            throw state.CreateTerminalException();

        try
        {
            if (state.Outcome == L00CScenarioOutcome.RunCompleted) return PublishCompletionOnce();
            if (TryStop()) return ThrowStopped();

            if (state.Stage == L00CScenarioStage.IterationVerified)
            {
                state = state.AdvanceVerifiedIteration(adapter.ReadMonotonicTimestamp());
                WriteNewTraceEntries();
                return state.Outcome == L00CScenarioOutcome.RunCompleted && PublishCompletionOnce();
            }

            if (state.IsWaiting)
            {
                long timestamp = adapter.ReadMonotonicTimestamp();
                if (adapter.TryObserve(screenManager, state, readyEventSessionOrdinal,
                        out L00CScenarioObservation? observation) && observation is not null)
                {
                    state = state.AcceptObservation(observation, timestamp);
                    if (observation.Kind == L00CScenarioObservationKind.SessionReady)
                        readyEventSessionOrdinal = 0;
                }
                else state = state.Poll(timestamp);
                WriteNewTraceEntries();
                if (state.IsTerminal) throw state.CreateTerminalException();
                return false;
            }

            if (state.PendingAction == L00CScenarioActionKind.None)
                throw new L00CScenarioException("L00C_S2_HOST_STAGE_UNMAPPED", state.Stage,
                    "running state is neither wait, action, iteration verification, nor completion");

            state = state.BeginAction(adapter.ReadMonotonicTimestamp());
            WriteNewTraceEntries();
            if (TryStop()) return ThrowStopped();

            adapter.ExecuteNativeAction(screenManager, state);
            state = state.MarkNativeEffectCompleted(adapter.ReadMonotonicTimestamp());
            WriteNewTraceEntries();
            if (TryStop()) return ThrowStopped();

            state = state.CompleteAction(adapter.ReadMonotonicTimestamp());
            WriteNewTraceEntries();
            if (state.IsTerminal) throw state.CreateTerminalException();
            return false;
        }
        catch (L00CScenarioException exception)
        {
            state = state.FailExternal(state.LastTimestamp, exception.Code, exception.Invariant);
            WriteNewTraceEntries();
            WriteTerminalDiagnostic(state.TerminalCode!, state.Stage, state.TerminalInvariant!);
            throw new L00CScenarioException(state.TerminalCode!, state.Stage, state.TerminalInvariant!,
                exception.InnerException ?? exception);
        }
        catch (InvalidOperationException exception)
        {
            string code = "L00C_S2_ADAPTER_INVALID_OPERATION_AT_" + state.Stage.ToString().ToUpperInvariant() +
                "_" + state.ActionProgress.ToString().ToUpperInvariant();
            string invariant = "adapter invalid operation: " + exception.Message;
            state = state.FailExternal(state.LastTimestamp, code, invariant);
            WriteNewTraceEntries();
            WriteTerminalDiagnostic(state.TerminalCode!, state.Stage, state.TerminalInvariant!);
            throw new L00CScenarioException(state.TerminalCode!, state.Stage, state.TerminalInvariant!, exception);
        }
        catch (Exception exception)
        {
            string type = exception.GetType().Name.ToUpperInvariant();
            string code = "L00C_S2_UNMAPPED_" + type + "_AT_" + state.Stage.ToString().ToUpperInvariant() +
                "_" + state.ActionProgress.ToString().ToUpperInvariant();
            string invariant = "adapter exception type=" + exception.GetType().FullName + ";message=" + exception.Message;
            state = state.FailExternal(state.LastTimestamp, code, invariant);
            WriteNewTraceEntries();
            WriteTerminalDiagnostic(state.TerminalCode!, state.Stage, state.TerminalInvariant!);
            throw new L00CScenarioException(state.TerminalCode!, state.Stage, state.TerminalInvariant!, exception);
        }
    }

    // The process-wide controller forwards only the generation-correlated
    // ready event ordinal. No API/world/client object is captured or retained.
    internal void SignalLevelFinalize(int sessionOrdinal)
    {
        if (state.IsTerminal)
            throw new L00CScenarioException("L00C_S2_READY_EVENT_AFTER_TERMINAL", state.Stage,
                "terminal host cannot receive another ready event");
        if (sessionOrdinal != state.ExpectedSessionOrdinal ||
            state.Stage is not (L00CScenarioStage.WaitCreatedAReady or
                L00CScenarioStage.WaitReopenedAReady or L00CScenarioStage.WaitCreatedBReady))
            throw new L00CScenarioException("L00C_S2_READY_EVENT_STATE_MISMATCH", state.Stage,
                "expectedSession=" + state.ExpectedSessionOrdinal + ";actualSession=" + sessionOrdinal);
        if (readyEventSessionOrdinal != 0)
            throw new L00CScenarioException("L00C_S2_READY_EVENT_DUPLICATE", state.Stage,
                "one ready event is already pending for this session");
        readyEventSessionOrdinal = sessionOrdinal;
    }

    internal L00CScenarioState SnapshotForTests() => state;

    private bool TryStop()
    {
        string? code = adapter.ReadStopCode();
        if (code is null) return false;
        state = state.Stop(adapter.ReadMonotonicTimestamp(), code);
        WriteNewTraceEntries();
        return true;
    }

    private bool ThrowStopped() => throw state.CreateTerminalException();

    private bool PublishCompletionOnce()
    {
        if (!completionPublished)
        {
            adapter.CompleteRun(state);
            completionPublished = true;
            WriteTerminalDiagnostic("L00C_S2_RUN_COMPLETED_NOT_T00_06_PASS", state.Stage,
                "five iterations and final menu observed; independent T00-06 acceptance remains external");
        }
        return true;
    }

    private void WriteNewTraceEntries()
    {
        while (writtenTraceEntries < state.Trace.Count)
        {
            L00CScenarioTraceEntry entry = state.Trace[writtenTraceEntries];
            string path = Path.Combine(evidenceDirectory, "progress-" + entry.Sequence.ToString("D4") + ".json");
            string json = "{\"schema\":\"l00c-s2-progress-v1\",\"sequence\":" + entry.Sequence +
                ",\"monotonicTimestamp\":" + entry.Timestamp + ",\"iteration\":" + state.Iteration +
                ",\"stage\":\"" + entry.Stage + "\",\"outcome\":\"" + state.Outcome +
                "\",\"code\":\"" + Escape(entry.Code) + "\",\"detail\":\"" + Escape(entry.Detail) + "\"}";
            WriteNew(path, json);
            writtenTraceEntries++;
        }
    }

    private void WriteTerminalDiagnostic(string code, L00CScenarioStage stage, string invariant)
    {
        string path = Path.Combine(evidenceDirectory, "terminal-" + code + ".json");
        if (File.Exists(path)) return;
        string json = "{\"schema\":\"l00c-s2-terminal-v1\",\"code\":\"" + Escape(code) +
            "\",\"state\":\"" + stage + "\",\"outcome\":\"" + state.Outcome +
            "\",\"iteration\":" + state.Iteration + ",\"invariant\":\"" + Escape(invariant) + "\"}";
        WriteNew(path, json);
    }

    private static string RequireNewEvidenceDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            throw new L00CScenarioException("L00C_S2_EVIDENCE_PATH_INVALID", null,
                "evidence directory must be absolute");
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), full,
                StringComparison.OrdinalIgnoreCase) || Directory.Exists(full) || File.Exists(full))
            throw new L00CScenarioException("L00C_S2_EVIDENCE_PATH_NOT_NEW_CANONICAL", null,
                "evidence directory must be a new canonical path");
        return full;
    }

    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void RequireDebugLaboratory()
    {
#if L00C_STANDALONE_ORACLE
        return;
#elif !DEBUG
        throw new L00CScenarioException("L00C_S2_DEBUG_BUILD_REQUIRED", null,
            "laboratory host is disabled outside Debug");
#else
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new L00CScenarioException("L00C_S2_DEBUG_LAB_AUTHORITY_REQUIRED", null,
                "Debugger.IsAttached and ISR_L00C_LAB=1 are both required");
#endif
    }
}
