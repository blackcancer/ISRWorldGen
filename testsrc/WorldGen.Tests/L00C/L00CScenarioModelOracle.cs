#nullable enable
#if L00C_STANDALONE_ORACLE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CScenarioModelOracle
{
    private static string root = string.Empty;
    private static L00CScenarioDefinition definition = null!;
    private static long now;

    private static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "l00c-s2-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            definition = L00CScenarioDefinition.CreateOwned("0123456789abcdef0123456789abcdef", root, 7);
            NormalFiveIterations();
            EveryWaitTimesOut();
            MissingReadyTimesOut();
            ExactAReopenRejectsBAndWrongGuid();
            InvalidCanonicalIdentityCodes();
            CancellationAroundEveryCreateAndSave();
            PartialStopIsStable();
            DiagnosticProvenanceSurvives();
            Console.WriteLine("{\"TestId\":\"L00-C-S2-IMMUTABLE-SCENARIO\",\"Status\":\"PASS\",\"Cases\":8,\"Iterations\":5,\"Sessions\":15,\"DedicatedSaves\":10}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void NormalFiveIterations()
    {
        now = 10;
        L00CScenarioState state = L00CScenarioState.Start(definition, now);
        while (!state.IsTerminal) state = NominalStep(state);
        Require(state.Outcome == L00CScenarioOutcome.RunCompleted, "normal run did not complete");
        Require(state.Stage == L00CScenarioStage.RunCompleted, "normal run terminal stage mismatch");
        Require(state.Trace[state.Trace.Count - 1].Code == "L00C_S2_RUN_COMPLETED_NOT_T00_06_PASS", "completion incorrectly claims PASS");
        Require(state.ObservedSavegameGuids.Count == 10, "new-save GUID count is not ten");
        Require(state.Trace.Count(entry => entry.Code == "L00C_S2_SESSION_READY_EXACT") == 15, "session count is not fifteen");
        Require(state.Trace.Count(entry => entry.Code == "L00C_S2_ITERATION_VERIFIED") == 5, "iteration count is not five");
        Require(definition.Iterations.SelectMany(value => new[] { value.A.CanonicalSavePath, value.B.CanonicalSavePath })
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10, "dedicated save paths are not ten distinct values");
    }

    private static void EveryWaitTimesOut()
    {
        now = 10;
        L00CScenarioState state = L00CScenarioState.Start(definition, now);
        var seen = new HashSet<L00CScenarioStage>();
        while (!state.IsTerminal)
        {
            if (state.IsWaiting)
            {
                seen.Add(state.Stage);
                L00CScenarioState timed = state.Poll(state.DeadlineTimestamp + 1);
                Require(timed.Outcome == L00CScenarioOutcome.Failed, "wait did not fail on timeout: " + state.Stage);
                Require(timed.TerminalCode == "L00C_S2_TIMEOUT_" + state.Stage.ToString().ToUpperInvariant(),
                    "timeout lost state provenance: " + state.Stage);
            }
            state = NominalStep(state);
        }
        Require(seen.SetEquals(new[]
        {
            L00CScenarioStage.WaitMenuBeforeCreateA,
            L00CScenarioStage.WaitCreatedAReady,
            L00CScenarioStage.WaitMenuAfterCreatedASave,
            L00CScenarioStage.WaitReopenedAReady,
            L00CScenarioStage.WaitMenuBeforeCreateB,
            L00CScenarioStage.WaitCreatedBReady,
            L00CScenarioStage.WaitMenuAfterCreatedBSave
        }), "not every wait state was reached");
    }

    private static void MissingReadyTimesOut()
    {
        now = 10;
        L00CScenarioState state = L00CScenarioState.Start(definition, now)
            .AcceptObservation(L00CScenarioObservation.MenuReady(), ++now);
        state = Action(state);
        L00CScenarioTarget target = state.ExpectedTarget;
        state = state.AcceptObservation(L00CScenarioObservation.SessionReady(1, target.CanonicalSavePath,
            GuidFor(1), true, false), ++now);
        Require(state.Stage == L00CScenarioStage.WaitCreatedAReady, "false ready event advanced state");
        state = state.Poll(state.DeadlineTimestamp + 1);
        Require(state.TerminalCode == "L00C_S2_TIMEOUT_WAITCREATEDAREADY", "missing ready event diagnostic mismatch");
    }

    private static void ExactAReopenRejectsBAndWrongGuid()
    {
        now = 10;
        L00CScenarioIteration plan = definition.GetIteration(1);
        string aGuid = GuidFor(1);
        L00CScenarioState state = L00CScenarioState.Start(definition, now)
            .AcceptObservation(L00CScenarioObservation.MenuReady(), ++now);
        state = Action(state).AcceptObservation(L00CScenarioObservation.SessionReady(1,
            plan.A.CanonicalSavePath, aGuid, true), ++now);
        state = Action(state).AcceptObservation(L00CScenarioObservation.SaveCommitted(1,
            plan.A.CanonicalSavePath, aGuid), ++now);
        state = Action(state);
        L00CScenarioState bPath = state.AcceptObservation(L00CScenarioObservation.SessionReady(2,
            plan.B.CanonicalSavePath, GuidFor(2), false), ++now);
        Require(bPath.TerminalCode == "L00C_S2_READY_SAVE_PATH_MISMATCH", "old/rebound B menu target was accepted for A reopen");
        L00CScenarioState wrongGuid = state.AcceptObservation(L00CScenarioObservation.SessionReady(2,
            plan.A.CanonicalSavePath, GuidFor(2), false), ++now);
        Require(wrongGuid.TerminalCode == "L00C_S2_REOPEN_SAVE_GUID_MISMATCH", "A reopen accepted another GUID");
    }

    private static void InvalidCanonicalIdentityCodes()
    {
        RequireCode(() => L00CScenarioObservation.SessionReady(1, @"relative\world.vcdbs", GuidFor(1), true),
            "L00C_S2_OBSERVED_SAVE_PATH_NOT_CANONICAL");
        RequireCode(() => L00CScenarioObservation.SessionReady(1, definition.GetIteration(1).A.CanonicalSavePath,
            GuidFor(1).ToUpperInvariant(), true), "L00C_S2_OBSERVED_SAVE_GUID_NOT_CANONICAL");
    }

    private static void CancellationAroundEveryCreateAndSave()
    {
        now = 10;
        int boundaries = 0;
        L00CScenarioState state = L00CScenarioState.Start(definition, now);
        while (!state.IsTerminal)
        {
            if (state.PendingAction is L00CScenarioActionKind.Create or L00CScenarioActionKind.SaveQuit)
            {
                L00CScenarioState before = state.Stop(++now, "L00C_S2_CANCELLED");
                Require(before.TerminalCode!.EndsWith("_NOTSTARTED", StringComparison.Ordinal), "before-action boundary lost");
                L00CScenarioState after = state.BeginAction(++now).MarkNativeEffectCompleted(++now)
                    .Stop(++now, "L00C_S2_INTERRUPTED");
                Require(after.TerminalCode!.EndsWith("_NATIVEEFFECTCOMPLETED", StringComparison.Ordinal), "after-effect boundary lost");
                boundaries += 2;
            }
            state = NominalStep(state);
        }
        Require(boundaries == 50, "ten creates and fifteen saves did not expose both interruption boundaries");
    }

    private static void PartialStopIsStable()
    {
        now = 10;
        L00CScenarioState state = L00CScenarioState.Start(definition, now)
            .AcceptObservation(L00CScenarioObservation.MenuReady(), ++now);
        state = Action(state);
        L00CScenarioState stopped = state.Stop(++now, "L00C_S2_OPERATOR_STOP");
        Require(stopped.Outcome == L00CScenarioOutcome.Stopped, "partial stop did not stop");
        Require(ReferenceEquals(stopped, stopped.Poll(long.MaxValue)), "terminal stop was not stable");
        Require(stopped.Stage != L00CScenarioStage.RunCompleted, "partial stop masqueraded as completion");
    }

    private static void DiagnosticProvenanceSurvives()
    {
        L00CScenarioState failed = L00CScenarioState.Start(definition, 10).CompleteAction(11);
        Require(failed.TerminalCode == "L00C_S2_ACTION_COMPLETION_INVALID", "invalid action code collapsed");
        L00CScenarioException exception = failed.CreateTerminalException();
        Require(exception.Code == failed.TerminalCode && exception.Stage == L00CScenarioStage.WaitMenuBeforeCreateA,
            "terminal exception lost code/state provenance");
        Require(exception.Message.IndexOf("progress=None", StringComparison.Ordinal) >= 0, "terminal exception lost invariant detail");
    }

    private static L00CScenarioState NominalStep(L00CScenarioState state)
    {
        if (state.PendingAction != L00CScenarioActionKind.None) return Action(state);
        int iteration = state.Iteration;
        L00CScenarioIteration plan = definition.GetIteration(iteration);
        string aGuid = GuidFor(iteration * 2 - 1);
        string bGuid = GuidFor(iteration * 2);
        return state.Stage switch
        {
            L00CScenarioStage.WaitMenuBeforeCreateA => state.AcceptObservation(L00CScenarioObservation.MenuReady(), ++now),
            L00CScenarioStage.WaitCreatedAReady => state.AcceptObservation(L00CScenarioObservation.SessionReady((iteration - 1) * 3 + 1, plan.A.CanonicalSavePath, aGuid, true), ++now),
            L00CScenarioStage.WaitMenuAfterCreatedASave => state.AcceptObservation(L00CScenarioObservation.SaveCommitted((iteration - 1) * 3 + 1, plan.A.CanonicalSavePath, aGuid), ++now),
            L00CScenarioStage.WaitReopenedAReady => state.AcceptObservation(L00CScenarioObservation.SessionReady((iteration - 1) * 3 + 2, plan.A.CanonicalSavePath, aGuid, false), ++now),
            L00CScenarioStage.WaitMenuBeforeCreateB => state.AcceptObservation(L00CScenarioObservation.SaveCommitted((iteration - 1) * 3 + 2, plan.A.CanonicalSavePath, aGuid), ++now),
            L00CScenarioStage.WaitCreatedBReady => state.AcceptObservation(L00CScenarioObservation.SessionReady(iteration * 3, plan.B.CanonicalSavePath, bGuid, true), ++now),
            L00CScenarioStage.WaitMenuAfterCreatedBSave => state.AcceptObservation(L00CScenarioObservation.SaveCommitted(iteration * 3, plan.B.CanonicalSavePath, bGuid), ++now),
            L00CScenarioStage.IterationVerified => state.AdvanceVerifiedIteration(++now),
            _ => throw new InvalidOperationException("Unhandled nominal stage " + state.Stage)
        };
    }

    private static L00CScenarioState Action(L00CScenarioState state)
        => state.BeginAction(++now).MarkNativeEffectCompleted(++now).CompleteAction(++now);

    private static string GuidFor(int number) => new Guid(number, unchecked((short)0xabcd), unchecked((short)0xabcd),
        new byte[] { 0xab, 0xcd, 0xef, 0xab, 0xcd, 0xef, 0xab, 0xcd }).ToString("D");

    private static void RequireCode(Action action, string code)
    {
        try { action(); }
        catch (L00CScenarioException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException("Expected exact diagnostic " + code);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
