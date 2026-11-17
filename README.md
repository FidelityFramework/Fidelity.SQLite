# Fidelity.SQLite

Generated Clef bindings for SQLite on Linux x86-64, with BAREWire boundary
contracts and a proof-substantiated integration suite. The package depends on
`../Fidelity.Platform/Profiles/Linux_x86_64_Default/Fidelity.Platform.fidproj`
and `../BAREWire/src/BAREWire.BindingMetadata.fidproj`.

## Generate and use

```sh
bash tools/generate.sh
bash tools/test.sh
```

Generation uses the already-built Farscape CLI and the installed SQLite header.
`pilot/sqlite.pilot.toml` is the source of the ABI annotations. Generated code
lives in `Bindings/SQLite`: `Types`, `Library`, `Connection`, `Statement`, and
`Function`, with four corresponding `*Api.clef` modules. For example, open
`Fidelity.SQLite.Statement.Api` to use the descriptor-preserving Layer 2 exports.
The generator emits no `nativeint` or `nativeptr` surface. It preserves SQLite's
native status codes; it does not invent an error convention.

`tools/generate.sh` restores the package-owned `Loading.clef` source and loading
selector after Farscape regenerates the project manifest.

## Zero-copy contracts

- `ReadOnlyBufferDescriptor` records explicit byte counts, text termination, and
  storage lifetime. `sqlite3_prepare_v2` has an explicit `count_parameter`.
- The generated blob/text bind declarations fix the destructor entry to
  `SQLITE_STATIC` (zero). `RetainedUntil` requires backing storage to remain live
  and unmodified until a declared retirement. `sqlite3_reset` preserves bindings;
  `sqlite3_clear_bindings` or `sqlite3_finalize` retires them.
- `BorrowedResultViewDescriptor` exposes optional read-only column views, with
  the matching `sqlite3_column_bytes` measurement and owner invalidation frontier.
  The measurement must precede the read. Generated `ResultRangeDescriptor`s state
  the documented nonnegative byte counts with their SQLite provenance.
- Nullable `CHandle` representation describes an ABI value. Ownership and cleanup
  obligations arise only from an actual `ResourceDescriptor` acquisition.

Storage remains caller-owned. Use a lexical `Region.create`/`Region.alloc` scope,
retire retained bindings before `Region.release`, and consume column views before
step/reset/finalize or suspension. `tests/Fidelity.SQLite.Tests/Positive.clef`
shows this pattern using the generated API modules and an explicitly annotated
single-exit function.

Automatic finalizing callback brackets are not supplied: `RetainedUntil` alone
cannot authorize consuming a caller's handle or prove that an arbitrary callback
neither finalizes nor escapes it. That requires an acquisition/release ownership
protocol and corresponding interprocedural source proofs.

SQLite documents the byte-count and view-validity contracts at
[Result Values From A Query](https://sqlite.org/c3ref/column_blob.html) and
[Obtaining SQL Values](https://sqlite.org/c3ref/value_blob.html).

## Loading

`Loading.clef` declares the dynamic `sqliteProcessLink : ProcessLinkContract`,
plus `StaticArchiveContract` and `ProcessLinkSpec` static-loading values.
`[platform].loading` selects the process-link **contract**. Static archive
selection belongs to `[link]`, for example:

```toml
[link]
libraries = ["sqlite3"]
archives = { sqlite3 = "/absolute/path/libsqlite3_probe.a" }
```

Changing the selected artifact does not change generated `.clef` or `.fidproj`
bytes. The static binding elision gate compares every generated surface file and
checks that a changed byte is detected.

## Verification

The 12-case xUnit suite checks package admission, regeneration and static elision,
positive PSG publication, native execution with ArtifactProof, and eight negative
controls. The controls require source-bound `CCS8424` (unbacked async retention),
`CCS8425` (early retirement, reset-as-retirement, stale views after step/reset/
finalize or suspension), and `CCS8426` (mutation while retained). Only exact,
dependent `CCS8403` cascades are admitted. One control also invokes Composer and
requires failure without an executable.

The positive gate requires two read-only crossings with `copies = 0`, two borrowed
views, valid PSG publication, and discharged `ZeroCopyBufferExtent`,
`BorrowedResultViewScope`, and `ResourceCleanup` obligations. The native gate uses
Composer's canonical LLVM/LLD path, verifies the executable-bound ArtifactProof
receipt, and requires exit zero and `sqlite:zero-copy:ok` on stdout.

The native fixture is a deterministic C probe archive, not a SQLite engine test.
C performs open/prepare and passes active handles into a closed Clef callback;
the generated Clef APIs perform binding, column access, reset, clear, finalize,
and close. A separate test-only owned acquisition/release adapter supplies the
exact `ResourceCleanup` account. The suite does not claim compiler-side
pointer-reference output-parameter support for open/prepare, or ownership proof
for the C-acquired handles.

The runner obtains Bozzetto `full_build` and `test_suite_run` leases with
`BOZZETTO_PROJECT` set to this checkout. It freezes the built Composer/Farscape
DLL distributions, uses HintPaths with `BuildProjectReferences=false`, checks
shared assembly hashes and source-input stability, and deletes its scratch tree.
Logs and receipts are under ignored `test-results`; remove that directory after
recording acceptance evidence. See `HANDOFF.md` for measured results and residuals.
