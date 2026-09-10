#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ISRWorldGen.L00C.Laboratory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ISRWorldGen.Tests.L00C;

[TestClass]
public sealed class L00CScenarioModelTests
{
    [TestMethod]
    public void ExactFiveIterationRunCompletesWithoutClaimingT0006Pass()
    {
        using var fixture = new ScenarioFixture();
        L00CScenarioState state = fixture.Start();
        long now = 10;

        for (int iteration = 1; iteration <= 5; iteration++)
        {
            L00CScenarioIteration plan = fixture.Definition.GetIteration(iteration);
            string aGuid = GuidFor(iteration * 2 - 1);
            string bGuid = GuidFor(iteration * 2);

            state = state.AcceptObservation(L00CScenarioObservation.MenuReady(), ++now);
            state = CompleteNativeAction(state, L00CScenarioActionKind.Create, ref now);
            state = state.AcceptObservation(L00CScenarioObservation.SessionReady((iteration - 1) * 3 + 1,
                plan.A.CanonicalSavePath, aGuid, true), ++now);
            state = CompleteNativeAction(state, L00CScenarioActionKind.SaveQuit, ref now);
            state = state.AcceptObservation(L00CScenarioObservation.SaveCommitted((iteration - 1) * 3 + 1,
                plan.A.CanonicalSavePath, aGuid), ++now);

            Assert.AreEqual(L00CScenarioActionKind.Reopen, state.PendingAction);
            state = CompleteNativeAction(state, L00CScenarioActionKind.Reopen, ref now);
            state = state.AcceptObservation(L00CScenarioObservation.SessionReady((iteration - 1) * 3 + 2,
                plan.A.CanonicalSavePath, aGuid, false), ++now);
            state = CompleteNativeAction(state, L00CScenarioActionKind.SaveQuit, ref now);
            state = state.AcceptObservation(L00CScenarioObservation.SaveCommitted((iteration - 1) * 3 + 2,
                plan.A.CanonicalSavePath, aGuid), ++now);

            Assert.AreEqual(L00CScenarioActionKind.Create, state.PendingAction);
            Assert.AreEqual(plan.B.CanonicalSavePath, state.ExpectedTarget.CanonicalSavePath);
            state = CompleteNativeAction(state, L00CScenarioActionKind.Create, ref now);
            state = state.AcceptObservation(L00CScenarioObservation.SessionReady(iteration * 3,
                plan.B.CanonicalSavePath, bGuid, true), ++now);
            state = CompleteNativeAction(state, L00CScenarioActionKind.SaveQuit, ref now);
            state = state.AcceptObservation(L00CScenarioObservation.SaveCommitted(iteration * 3,
                plan.B.CanonicalSavePath, bGuid), ++now);

            Assert.AreEqual(L00CScenarioStage.IterationVerified, state.Stage);
            state = state.AdvanceVerifiedIteration(++now);
        }

        Assert.AreEqual(L00CScenarioOutcome.RunCompleted, state.Outcome);
        Assert.AreEqual(L00CScenarioStage.RunCompleted, state.Stage);
        Assert.AreEqual("L00C_S2_RUN_COMPLETED_NOT_T00_06_PASS", state.Trace[state.Trace.Count - 1].Code);
        Assert.IsNull(state.TerminalCode, "RUN_COMPLETED is evidence production, not a T00-06 PASS verdict.");
        Assert.HasCount(10, state.ObservedSavegameGuids);
        Assert.AreEqual(15, state.Trace.Count(entry => entry.Code == "L00C_S2_SESSION_READY_EXACT"));
        Assert.AreEqual(5, state.Trace.Count(entry => entry.Code == "L00C_S2_ITERATION_VERIFIED"));
        Assert.AreEqual(10, fixture.Definition.Iterations.SelectMany(value => new[] { value.A.CanonicalSavePath, value.B.CanonicalSavePath }).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void EveryWaitStateHasAnIndependentMonotonicBoundedTimeout()
    {
        using var fixture = new ScenarioFixture(timeout: 7);
        L00CScenarioState state = fixture.Start();
        long now = 10;
        var seen = new HashSet<L00CScenarioStage>();

        while (!state.IsTerminal)
        {
            if (state.IsWaiting)
            {
                seen.Add(state.Stage);
                L00CScenarioState timedOut = state.Poll(state.DeadlineTimestamp + 1);
                Assert.AreEqual(L00CScenarioOutcome.Failed, timedOut.Outcome, state.Stage.ToString());
                Assert.AreEqual("L00C_S2_TIMEOUT_" + state.Stage.ToString().ToUpperInvariant(), timedOut.TerminalCode);
            }

            state = AdvanceNominalOneStep(state, fixture.Definition, ref now);
        }

        CollectionAssert.AreEquivalent(new[]
        {
            L00CScenarioStage.WaitMenuBeforeCreateA,
            L00CScenarioStage.WaitCreatedAReady,
            L00CScenarioStage.WaitMenuAfterCreatedASave,
            L00CScenarioStage.WaitReopenedAReady,
            L00CScenarioStage.WaitMenuBeforeCreateB,
            L00CScenarioStage.WaitCreatedBReady,
            L00CScenarioStage.WaitMenuAfterCreatedBSave
        }, seen.ToArray());
    }

    [TestMethod]
    public void MissingReadyEventNeverAdvancesAndEventuallyTimesOut()
    {
        using var fixture = new ScenarioFixture(timeout: 5);
        long now = 10;
        L00CScenarioState state = fixture.Start()
            .AcceptObservation(L00CScenarioObservation.MenuReady(), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.Create, ref now);
        L00CScenarioTarget target = state.ExpectedTarget;
        L00CScenarioState unchanged = state.AcceptObservation(L00CScenarioObservation.SessionReady(1,
            target.CanonicalSavePath, GuidFor(1), true, readyEventObserved: false), ++now);

        Assert.AreEqual(L00CScenarioStage.WaitCreatedAReady, unchanged.Stage);
        L00CScenarioState timedOut = unchanged.Poll(unchanged.DeadlineTimestamp + 1);
        Assert.AreEqual("L00C_S2_TIMEOUT_WAITCREATEDAREADY", timedOut.TerminalCode);
    }

    [TestMethod]
    public void ReopenRejectsOldMenuTargetOrBPathInsteadOfRebindingAnIndex()
    {
        using var fixture = new ScenarioFixture();
        long now = 10;
        L00CScenarioIteration plan = fixture.Definition.GetIteration(1);
        string aGuid = GuidFor(1);
        L00CScenarioState state = fixture.Start().AcceptObservation(L00CScenarioObservation.MenuReady(), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.Create, ref now);
        state = state.AcceptObservation(L00CScenarioObservation.SessionReady(1, plan.A.CanonicalSavePath, aGuid, true), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.SaveQuit, ref now);
        state = state.AcceptObservation(L00CScenarioObservation.SaveCommitted(1, plan.A.CanonicalSavePath, aGuid), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.Reopen, ref now);

        L00CScenarioState refused = state.AcceptObservation(
            L00CScenarioObservation.SessionReady(2, plan.B.CanonicalSavePath, GuidFor(2), false), ++now);

        Assert.AreEqual(L00CScenarioOutcome.Failed, refused.Outcome);
        Assert.AreEqual("L00C_S2_READY_SAVE_PATH_MISMATCH", refused.TerminalCode);
        StringAssert.Contains(refused.CreateTerminalException().Message, "state=WaitReopenedAReady");
    }

    [TestMethod]
    public void ReopenRequiresExactCreatedAGuidAndBMustRemainDistinct()
    {
        using var fixture = new ScenarioFixture();
        long now = 10;
        L00CScenarioIteration plan = fixture.Definition.GetIteration(1);
        string aGuid = GuidFor(1);
        L00CScenarioState state = fixture.Start().AcceptObservation(L00CScenarioObservation.MenuReady(), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.Create, ref now);
        state = state.AcceptObservation(L00CScenarioObservation.SessionReady(1, plan.A.CanonicalSavePath, aGuid, true), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.SaveQuit, ref now);
        state = state.AcceptObservation(L00CScenarioObservation.SaveCommitted(1, plan.A.CanonicalSavePath, aGuid), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.Reopen, ref now);

        L00CScenarioState wrongReopen = state.AcceptObservation(
            L00CScenarioObservation.SessionReady(2, plan.A.CanonicalSavePath, GuidFor(2), false), ++now);
        Assert.AreEqual("L00C_S2_REOPEN_SAVE_GUID_MISMATCH", wrongReopen.TerminalCode);

        state = state.AcceptObservation(L00CScenarioObservation.SessionReady(2, plan.A.CanonicalSavePath, aGuid, false), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.SaveQuit, ref now);
        state = state.AcceptObservation(L00CScenarioObservation.SaveCommitted(2, plan.A.CanonicalSavePath, aGuid), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.Create, ref now);
        L00CScenarioState reusedByB = state.AcceptObservation(
            L00CScenarioObservation.SessionReady(3, plan.B.CanonicalSavePath, aGuid, true), ++now);
        Assert.AreEqual("L00C_S2_NEW_SAVE_GUID_REUSED", reusedByB.TerminalCode);
    }

    [TestMethod]
    public void InvalidCanonicalPathAndGuidCarrySpecificDiagnosticCodes()
    {
        L00CScenarioException path = Assert.ThrowsExactly<L00CScenarioException>(() =>
            L00CScenarioObservation.SessionReady(1, @"relative\world.vcdbs", GuidFor(1), true));
        Assert.AreEqual("L00C_S2_OBSERVED_SAVE_PATH_NOT_CANONICAL", path.Code);

        using var fixture = new ScenarioFixture();
        L00CScenarioException guid = Assert.ThrowsExactly<L00CScenarioException>(() =>
            L00CScenarioObservation.SessionReady(1, fixture.Definition.GetIteration(1).A.CanonicalSavePath,
                GuidFor(1).ToUpperInvariant(), true));
        Assert.AreEqual("L00C_S2_OBSERVED_SAVE_GUID_NOT_CANONICAL", guid.Code);
    }

    [TestMethod]
    public void CancellationBeforeAndAfterEveryCreateAndSavePreservesBoundaryProvenance()
    {
        using var fixture = new ScenarioFixture();
        L00CScenarioState state = fixture.Start();
        long now = 10;
        int checkedBoundaries = 0;

        while (!state.IsTerminal)
        {
            if (state.PendingAction is L00CScenarioActionKind.Create or L00CScenarioActionKind.SaveQuit)
            {
                L00CScenarioState before = state.Stop(++now, "L00C_S2_CANCELLED");
                Assert.AreEqual(L00CScenarioOutcome.Stopped, before.Outcome);
                StringAssert.EndsWith(before.TerminalCode!, "_NOTSTARTED");
                checkedBoundaries++;

                L00CScenarioState after = state.BeginAction(++now).MarkNativeEffectCompleted(++now)
                    .Stop(++now, "L00C_S2_INTERRUPTED");
                Assert.AreEqual(L00CScenarioOutcome.Stopped, after.Outcome);
                StringAssert.EndsWith(after.TerminalCode!, "_NATIVEEFFECTCOMPLETED");
                checkedBoundaries++;
            }

            state = AdvanceNominalOneStep(state, fixture.Definition, ref now);
        }

        Assert.AreEqual(50, checkedBoundaries, "Ten creates and fifteen saves each have before/after interruption controls.");
    }

    [TestMethod]
    public void PartialStopIsTerminalStableAndNeverMasqueradesAsCompletion()
    {
        using var fixture = new ScenarioFixture();
        long now = 10;
        L00CScenarioState state = fixture.Start().AcceptObservation(L00CScenarioObservation.MenuReady(), ++now);
        state = CompleteNativeAction(state, L00CScenarioActionKind.Create, ref now);
        L00CScenarioState stopped = state.Stop(++now, "L00C_S2_OPERATOR_STOP");

        Assert.AreEqual(L00CScenarioOutcome.Stopped, stopped.Outcome);
        Assert.AreSame(stopped, stopped.Poll(long.MaxValue));
        Assert.AreSame(stopped, stopped.Stop(long.MaxValue, "L00C_S2_SECOND_STOP"));
        Assert.AreNotEqual(L00CScenarioStage.RunCompleted, stopped.Stage);
    }

    [TestMethod]
    public void InvalidActionDiagnosticRetainsOriginalStateAndInvariant()
    {
        using var fixture = new ScenarioFixture();
        L00CScenarioState failed = fixture.Start().CompleteAction(11);

        Assert.AreEqual(L00CScenarioStage.WaitMenuBeforeCreateA, failed.Stage);
        Assert.AreEqual("L00C_S2_ACTION_COMPLETION_INVALID", failed.TerminalCode);
        L00CScenarioException exception = failed.CreateTerminalException();
        Assert.AreEqual(failed.TerminalCode, exception.Code);
        Assert.AreEqual(failed.Stage, exception.Stage);
        StringAssert.Contains(exception.Message, "progress=None");
    }

    [TestMethod]
    public void ExternalDiagnosticBecomesTerminalWithoutLosingCodeStateOrInvariant()
    {
        using var fixture = new ScenarioFixture();
        L00CScenarioState state = fixture.Start().AcceptObservation(L00CScenarioObservation.MenuReady(), 11);
        state = state.BeginAction(12).FailExternal(12, "L00C_S2_NATIVE_CREATE_FAILED", "canonical A_1 create failed");

        Assert.AreEqual(L00CScenarioOutcome.Failed, state.Outcome);
        Assert.AreEqual(L00CScenarioStage.CreatingA, state.Stage);
        Assert.AreEqual(L00CScenarioActionProgress.Started, state.ActionProgress);
        Assert.AreEqual("L00C_S2_NATIVE_CREATE_FAILED", state.TerminalCode);
        Assert.AreEqual("canonical A_1 create failed", state.TerminalInvariant);
        Assert.AreEqual(state.TerminalCode, state.CreateTerminalException().Code);
    }

    [TestMethod]
    public void CompletionPublicationFailureOverridesRunCompletedWithoutClaimingPass()
    {
        using var fixture = new ScenarioFixture();
        L00CScenarioState state = fixture.Start();
        long now = 10;
        while (!state.IsTerminal) state = AdvanceNominalOneStep(state, fixture.Definition, ref now);

        state = state.FailExternal(now, "L00C_S2_COMPLETION_PUBLICATION_FAILED", "completion receipt was not durable");

        Assert.AreEqual(L00CScenarioOutcome.Failed, state.Outcome);
        Assert.AreEqual(L00CScenarioStage.RunCompleted, state.Stage);
        Assert.AreEqual("L00C_S2_COMPLETION_PUBLICATION_FAILED", state.TerminalCode);
        Assert.AreEqual("completion receipt was not durable", state.TerminalInvariant);
    }

    [TestMethod]
    public void DiagnosticTokensCannotEscapeTheEvidenceFilenameContract()
    {
        using var fixture = new ScenarioFixture();
        L00CScenarioState stopped = fixture.Start().Stop(11, "L00C_S2_BAD/FILE");
        L00CScenarioState failed = fixture.Start().FailExternal(11, "L00C_S2_BAD:FILE", "bad code");

        Assert.AreEqual("L00C_S2_STOP_CODE_INVALID", stopped.TerminalCode);
        Assert.AreEqual("L00C_S2_EXTERNAL_CODE_INVALID", failed.TerminalCode);
    }

    private static L00CScenarioState AdvanceNominalOneStep(L00CScenarioState state,
        L00CScenarioDefinition definition, ref long now)
    {
        if (state.Outcome == L00CScenarioOutcome.RunCompleted) return state;
        if (state.PendingAction != L00CScenarioActionKind.None)
            return CompleteNativeAction(state, state.PendingAction, ref now);

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
            _ => throw new AssertFailedException("Unhandled nominal stage " + state.Stage)
        };
    }

    private static L00CScenarioState CompleteNativeAction(L00CScenarioState state,
        L00CScenarioActionKind expected, ref long now)
    {
        Assert.AreEqual(expected, state.PendingAction);
        state = state.BeginAction(++now);
        Assert.AreEqual(L00CScenarioActionProgress.Started, state.ActionProgress);
        state = state.MarkNativeEffectCompleted(++now);
        Assert.AreEqual(L00CScenarioActionProgress.NativeEffectCompleted, state.ActionProgress);
        return state.CompleteAction(++now);
    }

    private static string GuidFor(int number) => new Guid(number, unchecked((short)0xabcd), unchecked((short)0xabcd),
        new byte[] { 0xab, 0xcd, 0xef, 0xab, 0xcd, 0xef, 0xab, 0xcd }).ToString("D");

    private sealed class ScenarioFixture : IDisposable
    {
        private readonly string root;

        internal ScenarioFixture(long timeout = 100)
        {
            root = Path.Combine(Path.GetTempPath(), "l00c-s2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Definition = L00CScenarioDefinition.CreateOwned("0123456789abcdef0123456789abcdef", root, timeout);
        }

        internal L00CScenarioDefinition Definition { get; }
        internal L00CScenarioState Start() => L00CScenarioState.Start(Definition, 10);

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
#endif
