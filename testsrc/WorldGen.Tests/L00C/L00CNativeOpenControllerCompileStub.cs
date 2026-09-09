#if L00C_STANDALONE_ORACLE
#nullable enable
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

// Compile-only seam for the driver/fixture oracles. The real Debug build and
// the executable LevelFinalize oracle use L00CProcessCampaignController.
internal static class L00CProcessCampaignController
{
    internal static L00CNativeOpenReservation BeginNativeOpen(int fixtureSequence) =>
        throw new System.NotSupportedException("Compile-only native-open seam.");
    internal static void CompleteNativeOpen(L00CNativeOpenReservation reservation) =>
        throw new System.NotSupportedException("Compile-only native-open seam.");
    internal static bool AbortNativeOpen(L00CNativeOpenReservation reservation) =>
        throw new System.NotSupportedException("Compile-only native-open seam.");
}
#endif
