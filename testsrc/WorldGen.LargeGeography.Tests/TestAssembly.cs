using Microsoft.VisualStudio.TestTools.UnitTesting;

// Whole-world rasters deliberately run sequentially inside one test process.
// The workflow parallelizes isolated jobs, not competing in-process allocations.
[assembly: DoNotParallelize]
