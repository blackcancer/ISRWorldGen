# Material cuts and curved opening — executable candidate

Base: PR7 `492ce5711706172e667aff15fe20a3d1f28a7eba`. PR5/6 and main are not changed.
Purpose: distinguish an internal failed link from a true separation, preserve all
material triangles/origins, and feed curved, conjugate dated opening patches into
the qualified conservative projector. This is NOT spontaneous fracture localisation.

`MaterialCutTopology.At` resolves incidence after dated upstream failures. Only a
complete cut disconnects the domain. `Open` supports exactly two fragments, one
boundary-to-boundary seam and complete translational histories starting at first
separation. It does not invent missing motion, solve pressure/toughness, or turn
plastic strain directly into failure. Closure, enclosed cuts, branches, subsequent
failure intervals, overlaps and missing histories are refused. Initially external
material remains unknown. Fragments retain full area, thickness and origin; actual
normal separation sweeps new ocean and its affine formation ages. Tangential slip
and common translation make no basalt.

The topology is evaluated on a conforming finite unwrapped triangle mesh. Initial
and final spatial footprints are checked by conservative projection. Arbitrary
crossing or overlapping meshes are not legal inputs. Continuous collision along a
curved motion path, rotations within phases and multi-fragment junctions are not
solved. Do not apply this limited opening to all world cells or label it a planet.

`RiftMaterialRaster.ProjectTriangles` reuses the existing integration/overlap code.
Legacy output hashes are pinned to the executed f3482012 campaign, independently
of the new algorithm. Thirty-four new C# checks plus the 115 existing relevant
checks are required. PNGs describe material fractions/ages, never elevation.
Three controlled 512-square examples use the complete million-unit reference.

Transition guard: isolated CI checkout/output per OS; no default generator, save,
registry, dependency or API change. Commit objects do not modify existing refs;
a new experimental branch is published without force and retained for review.
Output -> INCOMPLETE -> COMPLETE; exceptions retain FAILED. Reuse is refused with
return code 2, exact message and byte hashes unchanged. Partial output is never
retried in place. Legacy hashes, packets and raw fields are compared before
accepting test evidence. No independent-agent acceptance or merge is claimed.

Before CI: C# NOT_RUN; independent review NOT_RUN; geographic acceptance REJECTED
from earlier maps, erosion NOT_RUN. Final results must be appended truthfully.

Method distinction: ASPECT's ViscoPlastic material documentation describes yielding
and viscosity rescaling, not a geometric open crack. This increment deliberately
requires dated mechanical failure evidence instead of mislabelling plasticity.
https://aspect-documentation.readthedocs.io/en/latest/doxygen/classaspect_1_1MaterialModel_1_1ViscoPlastic.html
No new Vintage Story call; the CPU Core stays separate from ModSystem lifecycle.
