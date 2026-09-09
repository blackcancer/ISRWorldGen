// Bounded diagnostics used by the production-linked controller and executable
// oracle.  Exception messages are intentionally never written as evidence.
#nullable enable
using System;
using System.IO;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CCampaignInstallFailure
{
    internal const string ConstructionStatus = "controller-construction-fault";
    internal const string PumpEnqueueStatus = "pump-enqueue-fault";
    internal static string ConstructionDetail(string phase, Exception error) => phase + "-" + BoundedErrorCode(error);
    internal static string PumpEnqueueDetail(string phase, Exception error) => phase + "-" + BoundedErrorCode(error);
    private static string BoundedErrorCode(Exception error) => error switch
    {
        InvalidOperationException => "invalid-operation",
        ArgumentException => "argument",
        IOException => "io",
        _ => "unexpected"
    };
}
