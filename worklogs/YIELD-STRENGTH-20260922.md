# Regularized yield strength — implementation scope

Base: c0437a8953f9e0932606134bad882addb92ca4b6 (draft PR5); main remains 4f31f29a.
Bounded experiment: replace only the rheology in explicitly requested ViscoplasticWorld.Generate. No default-world change, erosion, native API or added dependency.

The new solver minimizes a convex four-point MAC quadrature potential. Unlike the older harmonic-corner linear sheet, this discretization uses one local invariant including shear and normal deformation. BOTH paired modes use the new quadrature. Homogeneous linear equivalence is tested; heterogeneous equality to the old discretization is NOT claimed.

The load law is ductile below Y; above Y a positive post-yield dashpot gives an explicitly regularized overstress. This is not exact ideal plasticity and not a tensile crack. Plastic excess is r-tau/(2 mu), is exactly zero below threshold and is tracked separately from viscous strain. The existing carrier-packet operator preserves its new semantic tag. The yield load (not the ductile viscosity) weakens from this memory. Source rates are computed on 128² mechanical quadrature, conservatively projected as constant rates to its 4x4 material subcells. They are NOT solved at material resolution.

Loads 700 continental / 350 oceanic are NORMALIZED MODEL LOADS, not Pa or inferred plate weights. Residual rate viscosity .15, strain scale .5, residual strength .35, support 10000 reference units are uncalibrated fixed experimental parameters. A bulk material mixture sets Y, not a depth-integrated Drucker-Prager law. The source is pressure independent. No healing, rupture graph, topology or ocean-birth change.

Fixed experiment: seeds -437287116,20260906,73, full atlas 1000000², materials 512², mechanics 128², duration36. Modes ductile/yield have identical initial volumes, material IDs and heights. Thermal state and old ocean source rule remain unchanged. Signed native elevation and PNG16 Y0..383 are preserved, no water mask, no color-threshold geography.

Preparation / interruption guard: additive candidate branch only, immutable Git objects based on the pinned parent; no force push or main update. Job-local new evidence paths; existing outputs must be rejected with original nonzero code and identical hashes. Original failed outputs are retained, no in-place retry or tolerance relaxation. Numerical acceptance requires true assembled residual <=1e-12, work/dissipation, positive strain balance and interplatform raw fields <=1e-8 with identical PNG codes. Independent agent review is NOT_RUN; branch remains experimental, no certification or lot DONE. In-memory iterates mutate only local work buffers and never a caller-owned published snapshot.

Implementation references (not calibration): ASPECT viscoplastic material documentation, https://aspect-documentation.readthedocs.io/en/latest/doxygen/classaspect_1_1MaterialModel_1_1ViscoPlastic.html ; Glerum et al., 2018, https://se.copernicus.org/articles/9/267/2018/ . They motivate separating ductile flow/yield/plastic strain, not the numerical parameters used here.
