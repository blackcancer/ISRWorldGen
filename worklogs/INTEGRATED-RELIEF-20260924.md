# Integrated relief continuation, 2026-09-24

Base read from GitHub: c7bd8ef3193ce43f872cceec9883bb598133f0ec (lithostatic-relief branch), which already contains a complete executed density/pressure/cooling experiment. Do not attribute that prior commit to a new implementation or replace main 4f31f29a silently.

Objective: substantive whole-world terrain changes with unmasked heightmaps, not another isolated diagnostic. Preserve the million-unit complete reference atlas. Erosion remains suspended and native Vintage Story integration is outside this experiment.

Environment preparation: local SDK absent and direct distribution DNS unavailable. A branch-scoped CI job retrieves the official SDK selected by the unchanged global.json, checks its SHA512 against official release metadata and publishes only that public archive and its receipt. No runner home, credentials, caches, user configuration or game files are copied. Archive remains a CI artifact with one-day retention, never a repository blob. This enables actual offline Core compilation and shorter numerical iteration.

Transitions: new unique output only -> official metadata selected -> archive fully received -> SHA512 verified -> completion receipt -> artifact published. Failure before receipt leaves an unusable partial directory in the disposable runner; the upload step is success-only. A fresh job starts in a new workspace. No mutation of persistent world state, registry or main. Local installation verifies the digest again and extracts under a new dedicated directory after path validation. A partial installation is never reused as a completed toolchain. This preparation is not a geographic acceptance, second-agent review or native-runtime qualification.
