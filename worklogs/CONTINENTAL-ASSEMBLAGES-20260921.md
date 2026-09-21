# Continental assemblages — bounded initial-material rework

Base: c74ca9019eb4eeb412adbf4a041c38d2f2b4058e. User-approved scope: realistic raw land/seabed relief, whole atlas at one million units, no erosion until reviewed. Preserve current material transport, strict numerical tolerances, native settings and registry.

## Defect and decision
The initial state assigned equal quotas to evenly separated cratons, at uniform continental thickness. The number of starting cratons consequently resembled a continent count. This is not a geological reason for continents of similar size. A plate is a mechanical domain, not a continent; old crust and inherited structures need a separate initial description.

The explicit candidate assembles adjacent Voronoi terranes into 3..6 unequal material provinces (or an explicit count), independently of plate count. Composite nuclei, area budgets, orientation-dependent accretion costs and different initial interior thicknesses are declared priors. Roots are drawn with a minimum separation, NOT placed at the farthest point. Adjacent provinces may assemble into the same landmass. This does NOT claim to simulate primordial plate formation or to derive initial shapes from solved forces.

The graph is built at a fixed control resolution. Near-isotropic physical site spacing respects rectangular full atlases. Subcell margins are initialized as positive mixtures of materials, not a filter over heightmaps. The same complete atlas is scaled to the world; no small-world crop. The reference metric remains a model coordinate system, not the claim that one block is one km.

For the three-seed comparison the mean continental inventory is copied from the old initial state. After heterogeneity is constructed, one explicit initial thickness multiplier enforces that inventory. It is recorded and bounded, never applied as a rendering contrast or sea-level change. Ocean inventory is separately recorded: this comparison matches continental volume, NOT all geological initial conditions. Densities and ocean ageing laws are unchanged. The initializer does not invent a random total plate weight, nor does it include mantle mass/rheology.

## Integration
`Generate` retains the previous route and checksum construction. `GenerateWithAssemblage` is a separate opt-in route using the same mechanical plates, transport, ocean carrier correction, creation/recycling, per-origin balances and column height conversion. Its initial-state checksum enters the history identity. Raw fields are frozen. Wrong seed/aspect/resolution inputs fail before evolution. Native world generation is NOT switched.

## Execution and publication boundary
Candidate code is first committed on `codex/continental-assemblages-20260921`, based on the pinned main. No registry or world files are written. Main is advanced only by a non-forced fast-forward after checking it still matches the pinned parent and checking the numerical campaign. Interrupting object creation leaves immutable unreferenced Git objects; no branch pointer changes until the final ref transition. A failed candidate remains in its branch, with logs and exact source; correction is a new commit, not a reset. Output paths must be new; existing campaign paths fail before writing. Hosted runners are ephemeral and only have contents:read; they never load a game, account or save. An independent second-agent review is NOT_RUN, not inferred from an author's test review.

## Qualification
The standalone console links actual Core and all existing material/tectonic checks, plus 16 checks of budgets, provenance, scaling, initial morphology, rectangular domains and read-only states. Real 512-square histories use the fixed seeds -437287116, 73, 20260906, duration 36, on Windows/Linux. Both initial states and candidate final solid elevation are emitted as float64 and fixed-range PNG16. The pre-existing platform tolerances and carrier checks are reused unchanged. Numerical success is not geographic acceptance.

The .NET SDK is not available in this authoring container; execution evidence must come from actual CI jobs, not Python substitutes. Final result and geographic verdict must be recorded after obtaining their artifacts. Erosion/native/MCP/full-mod build remain NOT_RUN. Full force balance, inherited-fault reactivation, thermal mantle columns, mechanical reassignment after accretion and flexural subduction are not implemented by this increment.

## Scientific motivation, not calibration
USGS, What is a tectonic plate?: https://pubs.usgs.gov/gip/dynamic/tectonic.html
Naliboff et al. (2017), Complex fault interaction controls continental rifting, Nature Communications 8, 1179: https://doi.org/10.1038/s41467-017-00904-x
These support separating mechanical plates from heterogeneous lithosphere and considering structural inheritance. They do not prescribe our numerical quota distribution or accretion prior, and do not validate the generated geography.
