using System.Runtime.CompilerServices;

// Only the opt-in raw relief executable can test the internal geometric kernels.
[assembly: InternalsVisibleTo("WorldGen.RawRelief")]
