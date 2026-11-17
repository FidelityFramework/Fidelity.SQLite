# Fidelity.SQLite binding increment — 2026-11-17

**Acceptance pending final unfiltered run; native witnessing remains blocked.**
Implementer: Codex. One read-only reviewer. No sibling edits or builds, commits,
pushes, secondary checkouts, or substituted native driver.

**Scope and owners.** Farscape owns generated declarations; BAREWire owns
contracts; Clef/CCS/Baker own settlement and proof obligations; PSG publishes;
Alex witnesses; Composer owns native compilation/linking and ArtifactProof.
This increment packages their existing outputs and tests their composition.
Clang/llvm-ar build only the independent C probe.

**Fixture correction.** The C shim calls open_v2, prepare_v2 with count 13, and
db_handle, passing active statement/connection handles to a closed Clef callback.
The callback creates its region before branches, binds bytes/text using generated
SQLITE_STATIC declarations, reads both columns and byte counts, resets, clears,
finalizes, closes, and releases the region on its sole exit path. Its return type
is inferred: an explicit result annotation made the current region pass see a
wrapped body instead of the exit sequence. The shim prints success only after
both native handles are closed and the callback returns zero.

The fixture keeps `[platform].loading` selecting the package's literal
ProcessLinkContract. `[link].archives` selects the actual probe archive; no
ProcessLinkSpec is substituted into the loading selector. The generated package
and pilot are unchanged by these fixture corrections.

A separate real open/close cycle uses test-only nullable-return acquisition and
nonnull release adapters, with an explicit flags argument and a distinct
OwnedConnection marker. Its ResourceDescriptor describes that cycle only. The
native gate requires ResourceCleanup for exactly SQLiteHarness.openConnection
and SQLiteHarness.closeConnection. It does not claim cleanup proof for the
callback's separate C-acquired database or compiler extent proof for the SQL
preparation in C.

**Rule controls and shape matrix.** The 12 tests comprise package ProjectChecker,
generator/pointer/static-elision, positive PSG publication, native execution and
ArtifactProof, and eight adversarial cases:

| Shape | Required source code |
| --- | --- |
| unbacked-await | CCS8424 |
| early-release | CCS8425 |
| reset-retains | CCS8425 |
| borrow-step | CCS8425 |
| borrow-reset | CCS8425 |
| borrow-finalize | CCS8425 |
| borrow-await | CCS8425 |
| mutate-retained | CCS8426 |

The two async controls permit only the exact dependent RunSynchronously and
boundary-argument CCS8403 messages. Outside async, the latter message is allowed
only when its entire source range equals an expected primary diagnostic's range.
This handles the observed same-site cascades on early-release, reset-retains and
mutate-retained without accepting unrelated errors. Every primary diagnostic must
point to Main.clef. One representative control also invokes Composer and asserts failure
without an executable; the remaining seven measure source refusal, not separate
native compilations. Each borrow control mutates both blob/text reads together;
its primary assertion establishes refusal of the combined program.
The generator gate compares every generated .clef/.fidproj byte and includes a
changed-byte discriminating mutation. The positive gate requires exactly two
read-only buffer uses (each copies=0), two borrowed result uses, and PSG integrity.
The native gate retains exit/stdout, receipt stage/hash, exact claim-anchor and
SAT-premise/UNSAT-obligation requirements; it is not weakened to pass.

**Residual R1 — nullable-handle publication/witness mismatch.** The measured
positive source gate passes. Native witnessing rejects generated nullable handle
calls with “Resource call lacks its source conversion and cleanup account.”
The published option<CHandle<sqlite3_stmt>> and option<CHandle<sqlite3>> formal
representations are Buffer(Some 16, Scalar(Integer(8, None))), not a Union.
Read-only review identified clef@026f0cb1efd862930a029fb5dfb05bc4570901ea,
`src/Compiler/Baker/Ingredients/ValueRepresentations.fs:72–103`: ordinary options
without owned-resource or callable-component accounts take the byte-buffer
fallback. Alex@764b9b86a905c2f43bdbd141ac6d7aa9c0840579,
`src/Alex/Dialects/Core/Types.fs:642–675`, requires a settled union containing a
CHandle for nullable borrowed arguments. Some syntax/type annotations cannot
change the generated formal's publication. No invented ownership descriptor or
replacement import was used to evade this boundary.

**Residual R2 — native fixture scope.** Generated open/prepare pointer references
are not admitted by the current compiler; db_handle's nonnullable opaque return
also receives CCS8403. The C callback adapter explicitly avoids those unsupported
Clef call shapes. These are scope limits, not proof of those generated adapters.

**Residual R3 — async refusal code.** Borrow-await reports CCS8424 at both
suspension sites, not the required CCS8425; the test remains red. Read-only review
confirmed `clef/src/Compiler/Nanopass/AsyncAndRegionSettlement.fs:1053–1071`
unconditionally chooses CCS8424 for live ReadOnlyView/borrowed-view slots.
It fails closed, but does not satisfy the requested diagnostic contract.

**Residual R4 — finalize owner correlation.** Borrow-finalize produces zero
ProjectChecker errors despite finalize preceding both column reads. The generated
borrow descriptors include finalize at Statement.clef:465/561. Read-only review
found that `BoundaryRecipes.fs:127–163` follows aliases but does not unwrap Some;
lines 3037–3038 and 3174–3183 compare raw statement identity against the optional
finalize argument's identity. The later reset supplies the required matching
invalidation, so it does not expose this earlier finalize. This supports an
optional-owner correlation gap; no native unsafe execution is claimed because
the native gate independently refuses these nullable-handle calls.

**Binding and leased evidence.** `bash tools/test.sh` sets
BOZZETTO_PROJECT=/home/hhh/repos/Fidelity.SQLite and obtains renewing full_build
and test_suite_run leases. Before building, it freezes existing Composer/Farscape
distributions under test-results/scratch, then builds only this test project with
HintPaths and BuildProjectReferences=false. It checks nine shared assembly hashes,
records observed source heads and exact binary/source hashes, and checks the
source closure after execution. Observed heads do not assert binary provenance.
Final counts, hashes and cleanup status will be recorded after the active run.

**Retrieval, context and growth.** No retrieval snapshot: retrieval.spkez.dev DNS
was unavailable. Bounded local reads used the user's named fidelity-lookup §3
exception. Bozzetto operational files used the outside-index exception. No index
freshness or pushed-source claim; no LAN worker. Project context came from
[Farscape #196](https://forge.spkez.dev/FidelityFramework/Farscape/issues/196),
[Farscape #220](https://forge.spkez.dev/FidelityFramework/Farscape/issues/220), and
[Composer #185](https://forge.spkez.dev/FidelityFramework/Composer/issues/185).
The requested local handoff template was absent; this follows the
[Bozzetto development page](https://forge.spkez.dev/FidelityFramework/Bozzetto/wiki/development).
SQLite base/head remain 144e72fb35a8346f53bee49caf547069bf731ea1;
`git diff --shortstat <base>..<head>` is empty because no commits were made.
New file owners/reasons: pilot plus five modules — Farscape package surface;
fidproj/Loading — package and load contracts; global.json — required .NET 10;
xUnit project/Suite/Positive/probe — acceptance fixtures; test.sh — leased runner
and dependency binding; HANDOFF — required evidence. README is updated. The
pre-existing untracked .gitignore is preserved. No new delivered files were
introduced by this fixture correction; ProbeHarness.clef is disposable test input.

**Review.** One reviewer accepted the adapter's actual resource cycle and the
reported positive evidence, identified the native representation mismatch, and
found no faithful fixture-only repair. No reviewer reran accepted gates.
