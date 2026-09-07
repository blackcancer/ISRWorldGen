using System.Text;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.ScaleProfiles;

internal sealed class NativeProfileCoordinator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object synchronization = new();
    private FrozenScaleProfile? publishedProfile;
    private volatile NativeProfileState state = NativeProfileState.Uninitialized;
    private NativeProfilePreparationSource preparationSource = NativeProfilePreparationSource.Unspecified;
    private int persistenceWrites;
    private int envelopeBytes;
    private Hash256 envelopeSha256 = Hash256.Zero;
    private bool gateCallbackRegistered;

    internal FrozenScaleProfile? PublishedProfile => Volatile.Read(ref publishedProfile);

    internal NativeProfilePreparation Prepare(
        NativeWorldSnapshot world,
        NativeProfileSelection selection,
        IFrozenProfileStore store,
        Func<NativeWorldSnapshot>? recaptureWorld = null,
        Action? beforeCommit = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(store);
        lock (synchronization)
        {
            if (state != NativeProfileState.Uninitialized)
            {
                return Reject(
                    GenerationFailureCode.InvalidInput,
                    "native-profile.prepare-once",
                    "Native profile preparation may run only once per server instance.");
            }

            state = NativeProfileState.Validating;
            preparationSource = world.IsNew
                ? NativeProfilePreparationSource.New
                : NativeProfilePreparationSource.Reload;
            NativeProfileError? worldError = ValidateWorld(world);
            if (worldError is not null)
            {
                return Reject(worldError);
            }

            byte[]? persisted;
            try
            {
                persisted = store.Read()?.ToArray();
            }
            catch (Exception exception)
            {
                return Reject(
                    GenerationFailureCode.CorruptData,
                    "native-profile.store-read",
                    $"Frozen profile store could not be read ({exception.GetType().Name}).");
            }

            return world.IsNew
                ? PrepareNewWorld(world, selection, store, persisted, recaptureWorld, beforeCommit)
                : PrepareExistingWorld(world, selection, persisted, recaptureWorld);
        }
    }

    internal NativeProfilePreparation DeactivateUnmarked()
    {
        lock (synchronization)
        {
            if (state != NativeProfileState.Uninitialized)
            {
                return Reject(
                    GenerationFailureCode.InvalidInput,
                    "native-profile.prepare-once",
                    "Native profile preparation may run only once per server instance.");
            }

            state = NativeProfileState.Inactive;
            return CreatePreparation(null, null);
        }
    }

    internal NativeWorldgenGateObservation ObserveWorldgenGate()
    {
        NativeProfileState observedState = state;
        FrozenScaleProfile? observedProfile = PublishedProfile;
        return new NativeWorldgenGateObservation(
            observedState == NativeProfileState.Frozen && observedProfile is not null,
            observedState,
            observedProfile?.Id);
    }

    internal NativeProfilePreparation FailClosed(NativeProfileError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (synchronization)
        {
            return Reject(error);
        }
    }

    private NativeProfilePreparation PrepareNewWorld(
        NativeWorldSnapshot world,
        NativeProfileSelection selection,
        IFrozenProfileStore store,
        byte[]? persisted,
        Func<NativeWorldSnapshot>? recaptureWorld,
        Action? beforeCommit)
    {
        if (persisted is not null)
        {
            return Reject(
                GenerationFailureCode.CorruptData,
                "native-profile.unexpected-persisted",
                "A new world unexpectedly already contains a frozen native profile envelope.");
        }

        if (!selection.IsSpecified)
        {
            state = NativeProfileState.Inactive;
            return CreatePreparation(null, null);
        }

        ScaleProfileDefinition? proposal = ResolveProposal(selection.ProfileId);
        if (proposal is null)
        {
            return Reject(
                GenerationFailureCode.InvalidInput,
                "native-profile.selection",
                "The explicitly selected scale profile is absent or unsupported.");
        }

        if (proposal.WidthBlocks != world.MapSizeX || proposal.LengthBlocks != world.MapSizeZ)
        {
            return Reject(
                GenerationFailureCode.InvalidInput,
                "native-profile.effective-dimensions",
                "The selected profile does not match the effective native X/Z dimensions.");
        }

        NativeProfileResult<NativeWorldConstraints> constraintsResult = CreateConstraints(world);
        if (!constraintsResult.IsSuccess)
        {
            return Reject(constraintsResult.Error!);
        }

        GenerationResult<FrozenScaleProfile> freezeResult = ScaleProfileValidator.ValidateAndFreeze(
            proposal,
            constraintsResult.Value!);
        if (freezeResult is GenerationFailure<FrozenScaleProfile> failure)
        {
            return Reject(failure.Error);
        }

        FrozenScaleProfile candidate = ((GenerationSuccess<FrozenScaleProfile>)freezeResult).Snapshot;
        NativeProfileError? exactDimensionError = ValidateExactProfileBinding(world, candidate, "native-profile.effective-dimensions");
        if (exactDimensionError is not null)
        {
            return Reject(exactDimensionError);
        }

        NativeProfileError? preStoreMutation = ValidateRecapturedWorld(world, recaptureWorld);
        if (preStoreMutation is not null)
        {
            return Reject(preStoreMutation);
        }

        byte[] pending;
        try
        {
            pending = NativeFrozenProfileEnvelopeCodec.Encode(
                world,
                candidate,
                NativeProfilePersistenceState.Pending);
            WriteEnvelope(store, pending);
        }
        catch (Exception exception)
        {
            return RejectAfterWrite(
                world,
                candidate,
                store,
                new NativeProfileError(
                GenerationFailureCode.CorruptData,
                "native-profile.store-write",
                $"Pending profile envelope could not be stored ({exception.GetType().Name})."));
        }

        byte[]? reread;
        try
        {
            reread = store.Read()?.ToArray();
        }
        catch (Exception exception)
        {
            return RejectAfterWrite(
                world,
                candidate,
                store,
                new NativeProfileError(
                GenerationFailureCode.CorruptData,
                "native-profile.store-reread",
                $"Pending profile envelope could not be reread ({exception.GetType().Name})."));
        }

        if (reread is null || !pending.AsSpan().SequenceEqual(reread))
        {
            return RejectAfterWrite(
                world,
                candidate,
                store,
                new NativeProfileError(
                GenerationFailureCode.CorruptData,
                "native-profile.store-reread",
                "Pending profile envelope differs from the bytes written in memory."));
        }

        RecordEnvelope(reread);

        NativeProfileResult<FrozenScaleProfile> strictReload = ReloadEnvelope(
            world,
            selection,
            reread,
            NativeProfilePersistenceState.Pending);
        if (!strictReload.IsSuccess)
        {
            return RejectAfterWrite(world, candidate, store, strictReload.Error!);
        }

        NativeProfileError? postStoreMutation = ValidateRecapturedWorld(world, recaptureWorld);
        if (postStoreMutation is not null)
        {
            return RejectAfterWrite(world, candidate, store, postStoreMutation);
        }

        try
        {
            beforeCommit?.Invoke();
            gateCallbackRegistered = beforeCommit is not null;
        }
        catch (Exception exception)
        {
            return RejectAfterWrite(
                world,
                candidate,
                store,
                new NativeProfileError(
                    GenerationFailureCode.InvalidInput,
                    "native-profile.worldgen-registration",
                    $"World generation gate could not be registered ({exception.GetType().Name})."));
        }

        byte[] committed;
        try
        {
            committed = NativeFrozenProfileEnvelopeCodec.Encode(
                world,
                strictReload.Value!,
                NativeProfilePersistenceState.Committed);
            WriteEnvelope(store, committed);
            RecordEnvelope(committed);
        }
        catch (Exception exception)
        {
            return RejectAfterWrite(
                world,
                candidate,
                store,
                new NativeProfileError(
                    GenerationFailureCode.CorruptData,
                    "native-profile.store-commit",
                    $"Committed profile envelope could not be stored ({exception.GetType().Name})."));
        }

        return Publish(strictReload.Value!);
    }

    private NativeProfilePreparation PrepareExistingWorld(
        NativeWorldSnapshot world,
        NativeProfileSelection selection,
        byte[]? persisted,
        Func<NativeWorldSnapshot>? recaptureWorld)
    {
        if (persisted is null)
        {
            if (selection.IsSpecified)
            {
                return Reject(
                    GenerationFailureCode.InvalidInput,
                    "native-profile.retroactive-activation",
                    "A pre-existing world cannot activate ISRWorldGen without a frozen native profile envelope.");
            }

            state = NativeProfileState.Inactive;
            return CreatePreparation(null, null);
        }

        NativeProfileResult<FrozenScaleProfile> reload = ReloadEnvelope(
            world,
            selection,
            persisted,
            NativeProfilePersistenceState.Committed);
        if (!reload.IsSuccess)
        {
            return Reject(reload.Error!);
        }

        NativeProfileError? worldMutation = ValidateRecapturedWorld(world, recaptureWorld);
        if (worldMutation is not null)
        {
            return Reject(worldMutation);
        }

        RecordEnvelope(persisted);
        return Publish(reload.Value!);
    }

    private static NativeProfileResult<FrozenScaleProfile> ReloadEnvelope(
        NativeWorldSnapshot world,
        NativeProfileSelection selection,
        ReadOnlySpan<byte> persisted,
        NativeProfilePersistenceState requiredPersistenceState)
    {
        NativeProfileResult<NativeFrozenProfileEnvelope> decoded = NativeFrozenProfileEnvelopeCodec.Decode(persisted);
        if (!decoded.IsSuccess)
        {
            return NativeProfileResult<FrozenScaleProfile>.Failure(
                decoded.Error!.Code,
                decoded.Error.Stage,
                decoded.Error.Details);
        }

        NativeFrozenProfileEnvelope envelope = decoded.Value!;
        if (envelope.PersistenceState != requiredPersistenceState)
        {
            string stage = envelope.PersistenceState switch
            {
                NativeProfilePersistenceState.Pending => "native-profile.envelope-pending",
                NativeProfilePersistenceState.Rejected => "native-profile.envelope-rejected",
                _ => "native-profile.envelope-state"
            };
            return NativeProfileResult<FrozenScaleProfile>.Failure(
                GenerationFailureCode.CorruptData,
                stage,
                $"Profile envelope state {envelope.PersistenceState} cannot activate this world.");
        }

        if (!EnvelopeMatchesWorld(envelope, world))
        {
            return NativeProfileResult<FrozenScaleProfile>.Failure(
                GenerationFailureCode.InvalidInput,
                "native-profile.world-mutation",
                "Effective save identity, dimensions, chunk size, or native rule set differs from the frozen envelope.");
        }

        NativeProfileResult<NativeWorldConstraints> constraintsResult = CreateConstraints(world);
        if (!constraintsResult.IsSuccess)
        {
            return NativeProfileResult<FrozenScaleProfile>.Failure(
                constraintsResult.Error!.Code,
                constraintsResult.Error.Stage,
                constraintsResult.Error.Details);
        }

        GenerationResult<FrozenScaleProfile> reload = FrozenScaleProfileCodec.Reload(
            envelope.GetProfileBytesCopy(),
            envelope.GeographyConfigHash,
            constraintsResult.Value!);
        if (reload is GenerationFailure<FrozenScaleProfile> failure)
        {
            return NativeProfileResult<FrozenScaleProfile>.Failure(
                failure.Error.Code,
                failure.Error.Stage,
                failure.Error.Details);
        }

        FrozenScaleProfile profile = ((GenerationSuccess<FrozenScaleProfile>)reload).Snapshot;
        if (selection.IsSpecified && !string.Equals(selection.ProfileId, profile.Id, StringComparison.Ordinal))
        {
            return NativeProfileResult<FrozenScaleProfile>.Failure(
                GenerationFailureCode.InvalidInput,
                "native-profile.selection-mutation",
                "The explicitly requested profile differs from the profile frozen for this world.");
        }

        NativeProfileError? exactBinding = ValidateExactProfileBinding(world, profile, "native-profile.world-mutation");
        if (exactBinding is not null)
        {
            return NativeProfileResult<FrozenScaleProfile>.Failure(
                exactBinding.Code,
                exactBinding.Stage,
                exactBinding.Details);
        }

        if (!string.Equals(profile.NativeRuleSetId, world.NativeRuleSetId, StringComparison.Ordinal) ||
            profile.NativeRuleSetVersion != world.NativeRuleSetVersion)
        {
            return NativeProfileResult<FrozenScaleProfile>.Failure(
                GenerationFailureCode.InvalidInput,
                "native-profile.world-mutation",
                "The reloaded profile native rule set differs from the current audited rule set.");
        }

        return NativeProfileResult<FrozenScaleProfile>.Success(profile);
    }

    private NativeProfilePreparation Publish(FrozenScaleProfile profile)
    {
        Volatile.Write(ref publishedProfile, profile);
        state = NativeProfileState.Frozen;
        return CreatePreparation(profile, null);
    }

    private NativeProfilePreparation RejectAfterWrite(
        NativeWorldSnapshot world,
        FrozenScaleProfile candidate,
        IFrozenProfileStore store,
        NativeProfileError error)
    {
        string tombstoneStatus;
        ClearEnvelope();
        try
        {
            byte[] tombstone = NativeFrozenProfileEnvelopeCodec.Encode(
                world,
                candidate,
                NativeProfilePersistenceState.Rejected);
            WriteEnvelope(store, tombstone);
            byte[]? reread = store.Read()?.ToArray();
            if (reread is not null && tombstone.AsSpan().SequenceEqual(reread))
            {
                RecordEnvelope(reread);
                tombstoneStatus = "Rejected tombstone persisted and reread.";
            }
            else
            {
                tombstoneStatus = "Rejected tombstone write returned but its readback could not be verified.";
            }
        }
        catch (Exception exception)
        {
            tombstoneStatus = $"Rejected tombstone persistence failed ({exception.GetType().Name}); residual Pending state remains non-activatable unless the store violated atomic replacement.";
        }

        return Reject(error with { Details = $"{error.Details} {tombstoneStatus}" });
    }

    private NativeProfilePreparation Reject(GenerationError error) => Reject(
        new NativeProfileError(error.Code, error.Stage, error.Details));

    private NativeProfilePreparation Reject(NativeProfileError error)
    {
        Volatile.Write(ref publishedProfile, null);
        state = NativeProfileState.Rejected;
        return CreatePreparation(null, error);
    }

    private NativeProfilePreparation Reject(
        GenerationFailureCode code,
        string stage,
        string details) => Reject(new NativeProfileError(code, stage, details));

    private NativeProfilePreparation CreatePreparation(
        FrozenScaleProfile? profile,
        NativeProfileError? error) => new(
            state,
            profile,
            error,
            new NativeProfilePersistenceEvidence(
                preparationSource,
                persistenceWrites,
                envelopeBytes,
                envelopeSha256,
                gateCallbackRegistered));

    private void WriteEnvelope(IFrozenProfileStore store, ReadOnlySpan<byte> content)
    {
        persistenceWrites = checked(persistenceWrites + 1);
        store.Write(content);
    }

    private void RecordEnvelope(ReadOnlySpan<byte> content)
    {
        if (content.Length is < 1 or > NativeFrozenProfileEnvelopeCodec.MaximumEnvelopeBytes)
        {
            throw new ArgumentException("Observed native profile envelope is outside its canonical bound.", nameof(content));
        }

        envelopeBytes = content.Length;
        envelopeSha256 = Hash256.Compute(content);
    }

    private void ClearEnvelope()
    {
        envelopeBytes = 0;
        envelopeSha256 = Hash256.Zero;
    }

    private static ScaleProfileDefinition? ResolveProposal(string? profileId)
    {
        if (!IsCanonicalIdentifier(profileId))
        {
            return null;
        }

        return ScaleProfileCatalog.Proposals.SingleOrDefault(
            profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal));
    }

    private static NativeProfileError? ValidateWorld(NativeWorldSnapshot world)
    {
        if (!IsCanonicalIdentifier(world.SavegameIdentifier) ||
            !IsCanonicalIdentifier(world.NativeRuleSetId) ||
            world.NativeRuleSetVersion == 0 ||
            world.ChunkSize <= 0 ||
            world.MaximumWorldSizeXz <= 0 || world.MaximumWorldSizeY <= 0 ||
            world.MapSizeX <= 0 || world.MapSizeY <= 0 || world.MapSizeZ <= 0 ||
            world.MapSizeX > world.MaximumWorldSizeXz ||
            world.MapSizeZ > world.MaximumWorldSizeXz ||
            world.MapSizeY > world.MaximumWorldSizeY ||
            world.MapSizeX % world.ChunkSize != 0 ||
            world.MapSizeY % world.ChunkSize != 0 ||
            world.MapSizeZ % world.ChunkSize != 0)
        {
            return new NativeProfileError(
                GenerationFailureCode.InvalidInput,
                "native-profile.native-world",
                "Effective native world values are non-canonical, out of audited limits, or not chunk-aligned.");
        }

        return null;
    }

    private static NativeProfileResult<NativeWorldConstraints> CreateConstraints(NativeWorldSnapshot world)
    {
        try
        {
            return NativeProfileResult<NativeWorldConstraints>.Success(world.CreateConstraints());
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return NativeProfileResult<NativeWorldConstraints>.Failure(
                GenerationFailureCode.InvalidInput,
                "native-profile.native-world",
                $"Native constraints could not be constructed ({exception.GetType().Name}).");
        }
    }

    private static NativeProfileError? ValidateExactProfileBinding(
        NativeWorldSnapshot world,
        FrozenScaleProfile profile,
        string stage)
    {
        if (profile.WidthBlocks != world.MapSizeX ||
            profile.HeightBlocks != world.MapSizeY ||
            profile.LengthBlocks != world.MapSizeZ)
        {
            return new NativeProfileError(
                GenerationFailureCode.InvalidInput,
                stage,
                "Frozen profile dimensions do not exactly match effective native X/Y/Z dimensions.");
        }

        return null;
    }

    private static NativeProfileError? ValidateRecapturedWorld(
        NativeWorldSnapshot expected,
        Func<NativeWorldSnapshot>? recaptureWorld)
    {
        if (recaptureWorld is null)
        {
            return null;
        }

        NativeWorldSnapshot actual;
        try
        {
            actual = recaptureWorld();
        }
        catch (Exception exception)
        {
            return new NativeProfileError(
                GenerationFailureCode.InvalidInput,
                "native-profile.world-mutation",
                $"Effective native world could not be recaptured ({exception.GetType().Name}).");
        }

        return SameWorldBinding(expected, actual)
            ? null
            : new NativeProfileError(
                GenerationFailureCode.InvalidInput,
                "native-profile.world-mutation",
                "Effective native world identity or X/Y/Z/chunk/rule-set values changed during preparation.");
    }

    private static bool EnvelopeMatchesWorld(NativeFrozenProfileEnvelope envelope, NativeWorldSnapshot world) =>
        string.Equals(envelope.SavegameIdentifier, world.SavegameIdentifier, StringComparison.Ordinal) &&
        envelope.MapSizeX == world.MapSizeX &&
        envelope.MapSizeY == world.MapSizeY &&
        envelope.MapSizeZ == world.MapSizeZ &&
        envelope.ChunkSize == world.ChunkSize &&
        string.Equals(envelope.NativeRuleSetId, world.NativeRuleSetId, StringComparison.Ordinal) &&
        envelope.NativeRuleSetVersion == world.NativeRuleSetVersion;

    private static bool SameWorldBinding(NativeWorldSnapshot expected, NativeWorldSnapshot actual) =>
        string.Equals(expected.SavegameIdentifier, actual.SavegameIdentifier, StringComparison.Ordinal) &&
        expected.MapSizeX == actual.MapSizeX &&
        expected.MapSizeY == actual.MapSizeY &&
        expected.MapSizeZ == actual.MapSizeZ &&
        expected.ChunkSize == actual.ChunkSize &&
        expected.MaximumWorldSizeXz == actual.MaximumWorldSizeXz &&
        expected.MaximumWorldSizeY == actual.MaximumWorldSizeY &&
        string.Equals(expected.NativeRuleSetId, actual.NativeRuleSetId, StringComparison.Ordinal) &&
        expected.NativeRuleSetVersion == actual.NativeRuleSetVersion;

    private static bool IsCanonicalIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return value.IsNormalized(NormalizationForm.FormC) && StrictUtf8.GetByteCount(value) <= 128;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
