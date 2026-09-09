// Pure deterministic lifecycle oracle for the in-process bootstrap lease.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ISRWorldGen.L00C.Laboratory;

internal interface IL00CManagerLeaseSeams
{
    DateTimeOffset UtcNow { get; }
    L00CManagerResolutionStatus Resolve(out object? manager);
    long Register(Action callback);
    void Unregister(long listenerId);
    void Install(object manager);
    void Receipt(string status, string detail);
    void Release();
}

/// <summary>
/// One-shot cancellation capability.  The owner predicate is evaluated at
/// cancellation time so a token retained by a superseded ModSystem is inert.
/// </summary>
internal sealed class L00CManagerLeaseToken
{
    private Func<bool>? cancelIfOwned;
    internal L00CManagerLeaseToken(Func<bool> cancelOwnedAction) { cancelIfOwned = cancelOwnedAction; }
    internal void Cancel() { Func<bool>? action = Interlocked.Exchange(ref cancelIfOwned, null); _ = action?.Invoke(); }
    internal void Complete() { Interlocked.Exchange(ref cancelIfOwned, null); }
}

/// <summary>
/// Shared ownership guard for the production Vintage Story adapter and its
/// executable oracle.  Every operation that can touch the session API requires
/// the lease to remain acquired; Release is one-shot and irrevocable.
/// </summary>
internal abstract class L00CManagerLeaseAdapter : IL00CManagerLeaseSeams
{
    private bool acquired = true;

    internal bool LeaseAcquired => acquired;
    public DateTimeOffset UtcNow { get { RequireAcquired(); return ReadUtcNow(); } }

    public L00CManagerResolutionStatus Resolve(out object? manager)
    {
        RequireAcquired();
        return ResolveAcquired(out manager);
    }

    public long Register(Action callback)
    {
        RequireAcquired();
        return RegisterAcquired(callback);
    }

    public void Unregister(long listenerId)
    {
        RequireAcquired();
        UnregisterAcquired(listenerId);
    }

    public void Install(object manager)
    {
        RequireAcquired();
        InstallAcquired(manager);
    }

    public void Receipt(string status, string detail) => WriteReceipt(status, detail);

    public void Release()
    {
        if (!acquired) return;
        acquired = false;
        ReleaseAcquired();
    }

    protected abstract DateTimeOffset ReadUtcNow();
    protected abstract L00CManagerResolutionStatus ResolveAcquired(out object? manager);
    protected abstract long RegisterAcquired(Action callback);
    protected abstract void UnregisterAcquired(long listenerId);
    protected abstract void InstallAcquired(object manager);
    protected abstract void WriteReceipt(string status, string detail);
    protected abstract void ReleaseAcquired();

    private void RequireAcquired()
    {
        if (!acquired) throw new InvalidOperationException("L00-C manager API lease is no longer acquired.");
    }
}

/// <summary>Injectable model of the production lease's terminal ordering.</summary>
internal sealed class L00CManagerLeaseLifecycle
{
    private const int MaximumAttempts = 600;
    private readonly IL00CManagerLeaseSeams seams;
    private readonly DateTimeOffset deadline;
    private long listenerId;
    private int attempts;
    private bool terminal;
    private bool installed;
    internal L00CManagerLeaseLifecycle(IL00CManagerLeaseSeams value) { seams = value; deadline = value.UtcNow.AddSeconds(30); }
    internal List<string> Receipts { get; } = new();
    internal bool ListenerActive => listenerId != 0;
    internal bool Terminal => terminal;
    internal bool Installed => installed;
    internal int Attempts => attempts;

    internal void Start()
    {
        if (terminal || installed || ListenerActive) return;
        TryResolve(initial: true);
    }
    internal void Dispose() => Terminalize("disposed");
    internal void Tick()
    {
        if (terminal || !ListenerActive) return;
        attempts++;
        if (attempts > MaximumAttempts || seams.UtcNow >= deadline) { Terminalize("timeout"); return; }
        TryResolve(initial: false);
    }
    private void TryResolve(bool initial)
    {
        L00CManagerResolutionStatus status;
        object? manager;
        try { status = seams.Resolve(out manager); }
        catch { Terminalize("resolver-fault"); return; }
        if (status == L00CManagerResolutionStatus.Ready)
        {
            // A listener must disappear before the production controller pump is queued.
            if (!StopListener("handoff-unregister-fault")) return;
            // Install must consume the still-acquired session context.  Only a
            // successful atomic controller/pump publication transfers ownership;
            // then the short API lease can be severed.
            try
            {
                seams.Install(manager!);
                installed = true;
                seams.Release();
                Publish(initial ? "ready-immediate" : "ready", "manager-handoff");
            }
            catch { Terminalize("install-pump-fault"); }
            return;
        }
        if (status == L00CManagerResolutionStatus.ApiTypeMismatch) { Terminalize("api-type-mismatch"); return; }
        if (!ListenerActive)
        {
            try { listenerId = seams.Register(Tick); Publish("waiting-" + status, status.ToString()); }
            catch { Terminalize("listener-register-fault"); }
        }
    }
    private bool StopListener(string failure)
    {
        if (!ListenerActive) return true;
        long id = listenerId;
        try { seams.Unregister(id); listenerId = 0; return true; }
        catch { listenerId = 0; terminal = true; Publish(failure, "unregister"); seams.Release(); return false; }
    }
    private void Terminalize(string status)
    {
        if (terminal) return;
        terminal = true;
        if (listenerId != 0)
        {
            long id = listenerId; listenerId = 0;
            try { seams.Unregister(id); }
            catch { status += "-unregister-fault"; }
        }
        Publish(status, "terminal");
        seams.Release();
    }
    private void Publish(string status, string detail) { Receipts.Add(status); seams.Receipt(status, detail); }
}

/// <summary>
/// Exact active/pump publication transaction used by the process controller.
/// Queue failure must leave no published active controller or queued-pump bit.
/// </summary>
internal sealed class L00CProcessCampaignInstallTransaction<T> where T : class
{
    internal T? Active { get; private set; }
    internal bool PumpQueued { get; private set; }
    internal void Install(T candidate, Action<T> enqueue)
    {
        if (Active is not null || PumpQueued) throw new InvalidOperationException("L00-C controller transaction is already active.");
        Active = candidate; PumpQueued = true;
        try { enqueue(candidate); }
        catch { Active = null; PumpQueued = false; throw; }
    }
    internal bool TrySignalSameRoot(string requestedRoot, Func<T, string> rootOf, Action<T> signal)
    {
        T? current = Active;
        if (current is null) return false;
        if (!string.Equals(rootOf(current), requestedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C refuses a second laboratory root in the same client process.");
        signal(current);
        return true;
    }
    internal void Clear(T candidate)
    {
        if (ReferenceEquals(Active, candidate)) { Active = null; PumpQueued = false; }
    }
    internal void PumpDequeued(T candidate)
    {
        if (ReferenceEquals(Active, candidate)) PumpQueued = false;
    }
    internal bool AbortIfSessionOwned<TSession>(
        TSession session,
        Func<T, TSession?> sessionOf,
        Action<T> abort) where TSession : class
    {
        T? current = Active;
        if (current is null || !ReferenceEquals(sessionOf(current), session)) return false;
        abort(current);
        // Compensation remains exact even if the controller abort callback
        // cannot reach its normal singleton cleanup.
        if (ReferenceEquals(Active, current)) Clear(current);
        return true;
    }
}

/// <summary>Immutable process and transaction provenance for one F5 child.</summary>
internal sealed class L00CF5LaunchIdentity
{
    internal L00CF5LaunchIdentity(
        string transactionId,
        string nonce,
        string transactionDirectory,
        string laboratoryRoot,
        string solutionPath,
        int visualStudioProcessId,
        int processId,
        string processStartUtc,
        bool debuggerAttached,
        string executablePath,
        string commandLine,
        IReadOnlyList<string> arguments)
    {
        TransactionId = Required(transactionId, nameof(transactionId));
        Nonce = Required(nonce, nameof(nonce));
        TransactionDirectory = Required(transactionDirectory, nameof(transactionDirectory));
        LaboratoryRoot = Required(laboratoryRoot, nameof(laboratoryRoot));
        SolutionPath = Required(solutionPath, nameof(solutionPath));
        if (visualStudioProcessId <= 0 || processId <= 0) throw new InvalidOperationException("L00-C F5 process identity must be positive.");
        VisualStudioProcessId = visualStudioProcessId;
        ProcessId = processId;
        ProcessStartUtc = Required(processStartUtc, nameof(processStartUtc));
        DebuggerAttached = debuggerAttached;
        ExecutablePath = Required(executablePath, nameof(executablePath));
        CommandLine = Required(commandLine, nameof(commandLine));
        if (arguments is null || arguments.Count > 64) throw new InvalidOperationException("L00-C F5 argument vector is absent or unbounded.");
        var copy = new string[arguments.Count];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index] ?? throw new InvalidOperationException("L00-C F5 argument vector contains null.");
            if (argument.Length > 4096) throw new InvalidOperationException("L00-C F5 argument is unbounded.");
            copy[index] = argument;
        }
        Arguments = copy;
    }

    internal string TransactionId { get; }
    internal string Nonce { get; }
    internal string TransactionDirectory { get; }
    internal string LaboratoryRoot { get; }
    internal string SolutionPath { get; }
    internal int VisualStudioProcessId { get; }
    internal int ProcessId { get; }
    internal string ProcessStartUtc { get; }
    internal bool DebuggerAttached { get; }
    internal string ExecutablePath { get; }
    internal string CommandLine { get; }
    internal IReadOnlyList<string> Arguments { get; }

    internal bool SameAs(L00CF5LaunchIdentity other)
    {
        if (other is null || TransactionId != other.TransactionId || Nonce != other.Nonce ||
            TransactionDirectory != other.TransactionDirectory || LaboratoryRoot != other.LaboratoryRoot ||
            SolutionPath != other.SolutionPath || VisualStudioProcessId != other.VisualStudioProcessId ||
            ProcessId != other.ProcessId || ProcessStartUtc != other.ProcessStartUtc ||
            DebuggerAttached != other.DebuggerAttached || ExecutablePath != other.ExecutablePath ||
            CommandLine != other.CommandLine || Arguments.Count != other.Arguments.Count) return false;
        for (int index = 0; index < Arguments.Count; index++)
            if (!string.Equals(Arguments[index], other.Arguments[index], StringComparison.Ordinal)) return false;
        return true;
    }

    private static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768)
            throw new InvalidOperationException("L00-C F5 identity field is absent or unbounded: " + name + ".");
        return value;
    }
}

/// <summary>
/// Durable one-writer/idempotent-reader receipt.  A second StartClientSide in
/// the same child validates the complete identity and never rewrites the file.
/// </summary>
internal static class L00CF5LaunchAcquisition
{
    private const string Protocol = "l00c-f5-debug-transaction-v2";
    private const int MaximumReceiptBytes = 65536;
    private static readonly object ReceiptGate = new();
    private static string? cachedReceiptPath;
    private static L00CF5LaunchIdentity? cachedIdentity;
    private static byte[]? cachedReceiptBytes;

    internal static void Record(string transactionDirectory, L00CF5LaunchIdentity identity, DateTimeOffset recordedUtc)
    {
        lock (ReceiptGate) RecordLocked(transactionDirectory, identity, recordedUtc);
    }

    private static void RecordLocked(string transactionDirectory, L00CF5LaunchIdentity identity, DateTimeOffset recordedUtc)
    {
        if (identity is null) throw new ArgumentNullException(nameof(identity));
        string directory = Path.GetFullPath(transactionDirectory);
        if (!string.Equals(directory, Path.GetFullPath(identity.TransactionDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C F5 receipt directory differs from its acquired identity.");
        RequireDirectory(directory);
        string receiptPath = Path.Combine(directory, "child-acquisition.json");
        string publishingPath = receiptPath + ".publishing";
        RequireNotDirectoryOrReparse(receiptPath);
        RequireNotDirectoryOrReparse(publishingPath);
        if (File.Exists(publishingPath))
            throw new InvalidOperationException("L00-C F5 child acquisition has an unresolved publishing residue.");
        if (File.Exists(receiptPath))
        {
            ValidateExisting(receiptPath, identity);
            return;
        }
        if (cachedReceiptPath is not null)
            throw new InvalidOperationException("L00-C F5 child acquisition receipt disappeared or changed transaction during this process.");

        byte[] bytes = Encoding.UTF8.GetBytes(ToJson(identity, recordedUtc));
        bool publishingCreated = false;
        try
        {
            using (var stream = new FileStream(publishingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                publishingCreated = true;
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(publishingPath, receiptPath);
            cachedReceiptPath = receiptPath;
            cachedIdentity = identity;
            cachedReceiptBytes = (byte[])bytes.Clone();
        }
        catch
        {
            // Delete only the exact bytes created by this invocation.  A swap,
            // reparse, directory, or partial write remains fail-closed evidence.
            if (publishingCreated && IsExactRegularFile(publishingPath, bytes)) File.Delete(publishingPath);
            throw;
        }
    }

    private static void ValidateExisting(string receiptPath, L00CF5LaunchIdentity expected)
    {
        FileInfo file = new(receiptPath);
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length <= 0 || file.Length > MaximumReceiptBytes)
            throw new InvalidOperationException("L00-C F5 child acquisition receipt is absent, redirected, empty, or unbounded.");
        if (cachedReceiptPath is null || cachedIdentity is null || cachedReceiptBytes is null ||
            !string.Equals(cachedReceiptPath, receiptPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C F5 child acquisition receipt was not published by this process instance.");
        if (!cachedIdentity.SameAs(expected))
            throw new InvalidOperationException("L00-C F5 child acquisition receipt belongs to another process or transaction provenance.");
        byte[] actual = File.ReadAllBytes(receiptPath);
        if (actual.Length != cachedReceiptBytes.Length)
            throw new InvalidOperationException("L00-C F5 child acquisition receipt was changed after publication.");
        for (int index = 0; index < actual.Length; index++)
            if (actual[index] != cachedReceiptBytes[index])
                throw new InvalidOperationException("L00-C F5 child acquisition receipt was changed after publication.");
    }

    private static string ToJson(L00CF5LaunchIdentity identity, DateTimeOffset recordedUtc)
    {
        var json = new StringBuilder(1024);
        json.Append("{\"schemaVersion\":2,\"protocol\":\"").Append(Quote(Protocol))
            .Append("\",\"status\":\"CHILD_ACQUIRED\",\"transactionId\":\"").Append(Quote(identity.TransactionId))
            .Append("\",\"nonce\":\"").Append(Quote(identity.Nonce))
            .Append("\",\"transactionDirectory\":\"").Append(Quote(identity.TransactionDirectory))
            .Append("\",\"laboratoryRoot\":\"").Append(Quote(identity.LaboratoryRoot))
            .Append("\",\"solutionPath\":\"").Append(Quote(identity.SolutionPath))
            .Append("\",\"visualStudioProcessId\":").Append(identity.VisualStudioProcessId)
            .Append(",\"processId\":").Append(identity.ProcessId)
            .Append(",\"processStartUtc\":\"").Append(Quote(identity.ProcessStartUtc))
            .Append("\",\"recordedUtc\":\"").Append(recordedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
            .Append("\",\"debuggerAttached\":").Append(identity.DebuggerAttached ? "true" : "false")
            .Append(",\"executablePath\":\"").Append(Quote(identity.ExecutablePath))
            .Append("\",\"commandLine\":\"").Append(Quote(identity.CommandLine))
            .Append("\",\"arguments\":[");
        for (int index = 0; index < identity.Arguments.Count; index++)
        {
            if (index > 0) json.Append(',');
            json.Append('"').Append(Quote(identity.Arguments[index])).Append('"');
        }
        return json.Append("]}").ToString();
    }

    private static string Quote(string value)
    {
        var escaped = new StringBuilder(value.Length + 16);
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\': escaped.Append("\\\\"); break;
                case '"': escaped.Append("\\\""); break;
                case '\b': escaped.Append("\\b"); break;
                case '\f': escaped.Append("\\f"); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                case '\t': escaped.Append("\\t"); break;
                default:
                    if (character < 0x20) escaped.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    else escaped.Append(character);
                    break;
            }
        }
        return escaped.ToString();
    }

    private static void RequireDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("L00-C F5 transaction directory is absent or redirected.");
    }

    private static void RequireNotDirectoryOrReparse(string path)
    {
        if (Directory.Exists(path)) throw new InvalidOperationException("L00-C F5 receipt path is a directory.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("L00-C F5 receipt path is redirected.");
    }

    private static bool IsExactRegularFile(string path, byte[] expected)
    {
        try
        {
            if (!File.Exists(path) || Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            byte[] actual = File.ReadAllBytes(path);
            if (actual.Length != expected.Length) return false;
            for (int index = 0; index < actual.Length; index++) if (actual[index] != expected[index]) return false;
            return true;
        }
        catch { return false; }
    }

}
