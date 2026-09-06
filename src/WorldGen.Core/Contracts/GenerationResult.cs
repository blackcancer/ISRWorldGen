using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Contracts;

public enum GenerationFailureCode
{
    InvalidInput = 1,
    UnsupportedVersion = 2,
    BudgetExceeded = 3,
    GeometryFailure = 4,
    NonConvergent = 5,
    CorruptData = 6,
    Cancelled = 7,
}

public sealed record GenerationError
{
    public GenerationError(
        GenerationFailureCode code,
        int nativeSeed,
        string stage,
        StableId zoneId,
        Hash256 inputHash,
        string details,
        bool canRetry)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown generation failure code.");
        }

        Code = code;
        NativeSeed = nativeSeed;
        Stage = CanonicalText.Require(stage, nameof(stage));
        ZoneId = zoneId;
        InputHash = inputHash;
        Details = CanonicalText.Require(details, nameof(details));
        CanRetry = canRetry;
    }

    public GenerationFailureCode Code { get; }

    public int NativeSeed { get; }

    public string Stage { get; }

    public StableId ZoneId { get; }

    public Hash256 InputHash { get; }

    public string Details { get; }

    public bool CanRetry { get; }
}

public abstract class GenerationResult<TSnapshot>
    where TSnapshot : class
{
    private protected GenerationResult(bool isSuccess) => IsSuccess = isSuccess;

    public bool IsSuccess { get; }

    public static GenerationResult<TSnapshot> Success(TSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new GenerationSuccess<TSnapshot>(snapshot);
    }

    public static GenerationResult<TSnapshot> Failure(GenerationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new GenerationFailure<TSnapshot>(error);
    }
}

public sealed class GenerationSuccess<TSnapshot> : GenerationResult<TSnapshot>
    where TSnapshot : class
{
    internal GenerationSuccess(TSnapshot snapshot)
        : base(true) => Snapshot = snapshot;

    public TSnapshot Snapshot { get; }
}

public sealed class GenerationFailure<TSnapshot> : GenerationResult<TSnapshot>
    where TSnapshot : class
{
    internal GenerationFailure(GenerationError error)
        : base(false) => Error = error;

    public GenerationError Error { get; }
}
