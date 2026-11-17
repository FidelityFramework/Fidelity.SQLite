namespace Fidelity.SQLite.Tests

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text.RegularExpressions
open System.Text.Json
open Xunit
open Clef.Compiler.Project
open Clef.Compiler.PSGSaturation.SemanticGraph.Types
module Diagnostics = Clef.Compiler.PSGSaturation.SemanticGraph.Diagnostics
module Boundary = Clef.Compiler.PSGSaturation.SemanticGraph.BoundaryEmission

[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()

module Harness =
    let root () = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
    let at path = Path.Combine(root (), path)
    let quote (s: string) = "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
    let accepted = function Result.Ok value -> value | Result.Error error -> failwithf "%A" error
    let sha path = File.ReadAllBytes path |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
    let run directory command args =
        let start = ProcessStartInfo(command, WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)
        for arg in args do start.ArgumentList.Add arg
        start.Environment["TMPDIR"] <- at "test-results/scratch/tmp"
        use child = Process.Start start
        let output = child.StandardOutput.ReadToEndAsync()
        let error = child.StandardError.ReadToEndAsync()
        if not (child.WaitForExit 300000) then
            child.Kill(true)
            failwithf "Timed out: %s %A" command args
        child.ExitCode, output.Result + error.Result
    let succeed dir cmd args =
        let code, output = run dir cmd args
        Assert.True((code = 0), output)
        output
    type Scratch(name: string) =
        let path = at ("test-results/scratch/" + name + "-" + Guid.NewGuid().ToString("N"))
        do Directory.CreateDirectory path |> ignore
        member _.Path = path
        interface IDisposable with member _.Dispose() = Directory.Delete(path, true)
    let check path = ProjectChecker.checkProject path |> accepted
    let errors (project: ProjectCheckResult) =
        Diagnostics.CheckResult.errors project.CheckResult
    let diagnosticText diagnostics =
        diagnostics |> List.map (fun (d: Diagnostics.Diagnostic) -> sprintf "%s:%d:%d %s %s" d.Range.File d.Range.Start.Line d.Range.Start.Column d.Code d.Message) |> String.concat "\n"
    let clean project =
        Assert.True(project.ParseErrors.IsEmpty, sprintf "%A" project.ParseErrors)
        Assert.True((errors project).IsEmpty, diagnosticText (errors project))
    // Test-only ABI adapter. SQLite's generated pointer-reference output parameters
    // remain on the C side; the callback receives a closed, active statement.
    let probeHarness = """module SQLiteHarness
open Fidelity.SQLite.Types
open BAREWire.Descriptors

[<FidelityExtern("sqlite3", "probe_invoke")>]
let invoke (callback: FnPtr<CHandle<sqlite3_stmt> -> CHandle<sqlite3> -> int>) : int = NativeDefault.zeroed ()
let invokeDescriptor: Expr<FunctionDescriptor> = <@ {
    CName = "probe_invoke"
    Parameters = [| { Name = "callback"; Type = Entry "SQLiteHarness.invokeInvocationContract"; PassBy = Value } |]
    ReturnType = Integer (Signed, 32); CallingConvention = CDecl; OwnershipTransfer = Undeclared
} @>
let invokeScope: Expr<ScopedCallbackDescriptor> = <@ { BAREWire.Descriptors.ScopedCallbackDescriptor.Binding = "SQLiteHarness.invoke"; Parameter = "callback" } @>
let invokeInvocationContract: Expr<CallbackInvocationContract> = <@ {
    Binding = "SQLiteHarness.invoke"; Parameter = 0
    Signature = {
        Parameters = [| { Name = "stmt"; Type = Pointer 64; PassBy = Value }; { Name = "db"; Type = Pointer 64; PassBy = Value } |]
        ReturnType = Integer (Signed, 32); CallingConvention = CDecl }
    Hypothesis = { Identity = "sqlite-probe-entry"; Version = "1"; Source = "probe.c:probe_invoke" }
    Scope = "SQLiteHarness.invokeScope"
} @>
type OwnedConnection = private | OpaqueOwnedConnection
[<FidelityExtern("sqlite3", "probe_open_owned")>]
let openConnection (flags: int) : option<CHandle<OwnedConnection>> = NativeDefault.zeroed ()
let openConnectionDescriptor: Expr<FunctionDescriptor> = <@ {
    CName = "probe_open_owned"; Parameters = [| { Name = "flags"; Type = Integer (Signed, 32); PassBy = Value } |]; ReturnType = Pointer 64
    CallingConvention = CDecl; OwnershipTransfer = Undeclared
} @>
[<FidelityExtern("sqlite3", "probe_close_owned")>]
let closeConnection (db: CHandle<OwnedConnection>) : int = NativeDefault.zeroed ()
let closeConnectionDescriptor: Expr<FunctionDescriptor> = <@ {
    CName = "probe_close_owned"
    Parameters = [| { Name = "db"; Type = Pointer 64; PassBy = Value } |]
    ReturnType = Integer (Signed, 32); CallingConvention = CDecl; OwnershipTransfer = Undeclared
} @>
let openConnectionResourceDescriptor: Expr<ResourceDescriptor> = <@ {
    Resource = OwnedNullableReturn { Binding = "SQLiteHarness.closeConnection"; Parameter = 0 }
    Hypothesis = { Identity = "sqlite-probe-owned-open"; Version = "1"; Source = "probe.c:probe_open_owned" }
} @>
"""
    let project directory (source: string) =
        File.WriteAllText(Path.Combine(directory, "ProbeHarness.clef"), probeHarness)
        let main = Path.Combine(directory, "Main.clef")
        File.WriteAllText(main, source)
        let path = Path.Combine(directory, "Probe.fidproj")
        File.WriteAllText(path, "[package]\nname = \"SQLiteProbe\"\nversion = \"0.1.0\"\n[compilation]\ntarget = \"cpu\"\n[build]\noutput_kind = \"console\"\nsources = [\"ProbeHarness.clef\", \"Main.clef\"]\n[platform]\ndescription = \"Fidelity.Platform.Linux_x86_64.Description.description\"\nloading = \"Fidelity.SQLite.Loading.sqliteProcessLink\"\nruntime_model = \"libc\"\nos = \"linux\"\narch = \"x86_64\"\n[dependencies]\nsqlite = { path = " + quote (at "Fidelity.SQLite.fidproj") + " }\nlibc = { path = " + quote (Path.GetFullPath(at "../Fidelity.Platform/Environments/Linux/x86_64/Fidelity.Libc.fidproj")) + " }\n")
        path
    let positive () = File.ReadAllText(at "tests/Fidelity.SQLite.Tests/Positive.clef")

    let tool kind name = at ("test-results/scratch/toolchain/" + kind + "/" + name)
    let compile directory path =
        let code, output = run directory "dotnet" [tool "composer" "Composer.dll"; "compile"; "--no-color"; path; "-k"; "-o"; Path.Combine(directory, "probe")]
        File.WriteAllText(at ("test-results/compiler-" + Path.GetFileName(directory).Split('-')[0] + ".log"), output)
        if code <> 0 then
            for path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories) do
                if [".ll"; ".bc"; ".mlir"] |> List.contains (Path.GetExtension path) then
                    let destination = at ("test-results/failure-" + Path.GetFileName(directory).Split('-')[0] + "/" + Path.GetRelativePath(directory, path))
                    Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
                    File.Copy(path, destination, true)
        let summary = output.Split('\n') |> Array.filter (fun line -> not (line.Contains(": info ")) && not (line.TrimStart().StartsWith("span:"))) |> String.concat "\n"
        code, summary
    let generate pilot directory =
        succeed directory "dotnet" [tool "farscape" "Farscape.Cli.dll"; "project"; "--project"; pilot] |> ignore
    let archive directory =
        let source = File.ReadAllText(at "tests/Fidelity.SQLite.Tests/probe.c")
        let symbols =
            Directory.GetFiles(at "Bindings/SQLite", "*.clef", SearchOption.AllDirectories)
            |> Array.collect (fun path ->
                Regex.Matches(File.ReadAllText path, "FidelityExtern\\(\"sqlite3\", \"([^\"]+)\"")
                |> Seq.cast<Match> |> Seq.map (fun m -> m.Groups[1].Value) |> Seq.toArray)
            |> Array.distinct
        let stubs = symbols |> Array.filter (fun symbol -> not (source.Contains(symbol + "("))) |> Array.map (fun symbol -> "int " + symbol + "(void) { return -999; }")
        let c = Path.Combine(directory, "probe.c")
        File.WriteAllText(c, source + "\n" + String.concat "\n" stubs)
        let obj = Path.Combine(directory, "probe.o")
        let archive = Path.Combine(directory, "libsqlite3_probe.a")
        succeed directory "clang" ["-O2"; "-c"; c; "-o"; obj] |> ignore
        succeed directory "llvm-ar" ["rcs"; archive; obj] |> ignore
        archive
    let selectArchive path archive =
        // Loading selects a ProcessLinkContract. Archive selection belongs to [link].
        File.AppendAllText(path, "[link]\nlibraries = [\"sqlite3\"]\narchives = { sqlite3 = " + quote archive + " }\n")
    let surface directory =
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        |> Array.filter (fun p -> p.EndsWith(".clef") || p.EndsWith(".fidproj"))
        |> Array.map (fun p -> Path.GetRelativePath(directory, p), File.ReadAllBytes p)
        |> Map.ofArray

module Gates =
    [<Fact>]
    let ``Package passes ProjectChecker with zero errors`` () =
        Harness.check (Harness.at "Fidelity.SQLite.fidproj") |> Harness.clean

    [<Fact>]
    let ``Region backed SQLite program publishes zero copy uses`` () =
        use work = new Harness.Scratch("positive")
        let checkedProject = Harness.project work.Path (Harness.positive ()) |> Harness.check
        Harness.clean checkedProject
        let boundary = Boundary.project checkedProject.CheckResult.Graph |> Harness.accepted
        Assert.Equal(2, boundary.ReadOnlyBufferUses.Count)
        Assert.Equal(2, boundary.BorrowedResultViewUses.Count)
        for use' in boundary.ReadOnlyBufferUses.Values do Assert.Equal(0, use'.Copies)
        let revision = Clef.Compiler.PSGSaturation.Publication.RevisionPublication.publish checkedProject.CheckResult.Graph |> Harness.accepted
        Assert.Empty(Fidelity.PSG.Integrity.check revision)

    [<Fact>]
    let ``Generator zero pointer surface and Static Binding Elision`` () =
        use work = new Harness.Scratch("generation")
        let archive = Harness.archive work.Path
        let generate name artifact =
            let directory = Path.Combine(work.Path, name)
            Directory.CreateDirectory(Path.Combine(directory, "pilot")) |> ignore
            let pilot = Path.Combine(directory, "pilot/sqlite.pilot.toml")
            let text = File.ReadAllText(Harness.at "pilot/sqlite.pilot.toml")
            File.WriteAllText(pilot, text.Replace("[output]", artifact + "\n[output]"))
            Harness.generate pilot directory
            let surface = Harness.surface directory
            Assert.Equal(10, surface.Count)
            for KeyValue(path, bytes) in surface do
                Assert.False(Regex.IsMatch(Text.Encoding.UTF8.GetString bytes, @"\b(nativeint|nativeptr)\b"), path)
                if path.EndsWith(".clef") then
                    Assert.True((bytes = File.ReadAllBytes(Harness.at path)), "Generated binding drift: " + path)
            surface
        let dynamic = generate "dynamic" ""
        let static' = generate "static" ("[library.artifact]\nkind = \"static-archive\"\npath = " + Harness.quote archive + "\nsha256 = " + Harness.quote (Harness.sha archive))
        Assert.True((dynamic = static'), "Static binding elision changed Clef/fidproj bytes")
        let mutant = static' |> Map.map (fun path bytes -> if path.EndsWith("Statement.clef") then Array.append bytes [|32uy|] else bytes)
        Assert.False((dynamic = mutant), "Byte comparison must detect a changed declaration")

    [<Theory>]
    [<InlineData("unbacked-await", "CCS8424")>]
    [<InlineData("early-release", "CCS8425")>]
    [<InlineData("reset-retains", "CCS8425")>]
    [<InlineData("borrow-step", "CCS8425")>]
    [<InlineData("borrow-reset", "CCS8425")>]
    [<InlineData("borrow-finalize", "CCS8425")>]
    [<InlineData("borrow-await", "CCS8425")>]
    [<InlineData("mutate-retained", "CCS8426")>]
    let ``Memory safety controls fail closed at the source`` (shape: string, code: string) =
        use work = new Harness.Scratch(shape)
        let positive = Harness.positive ()
        let source =
            match shape with
            | "unbacked-await" -> "module Refusal\nlet step () = async { return 1 }\n[<EntryPoint>]\nlet main _ =\n    let work = async {\n        let backing = [| 65; 66 |]\n        let view = ReadOnlyView.ofArray backing\n        let! value = step ()\n        return value + view.[0]\n    }\n    Async.RunSynchronously work\n"
            | "early-release" -> positive.Replace("    let stepped =", "    Region.release region\n    let stepped =").Replace("    Region.release region\n    result", "    result")
            | "reset-retains" -> positive.Replace("    let cleared = sqlite3_clear_bindings stmt", "    let cleared = sqlite3_reset stmt").Replace("    let finalized = sqlite3_finalize optionalStmt", "    Region.release region\n    let finalized = sqlite3_finalize optionalStmt").Replace("    Region.release region\n    result", "    result")
            | "mutate-retained" -> positive.Replace("    let stepped =", "    backing.[0] <- 66\n    let stepped =")
            | "borrow-await" ->
                positive.Replace("let runProbe", "let pause () = async { return 0 }\n\nlet runProbe")
                    .Replace("| Some bytes -> bytes.[0]", "| Some bytes ->\n            Async.RunSynchronously (async {\n                let! ignored = pause ()\n                return bytes.[0] + ignored\n            })")
            | event ->
                let call = match event with "borrow-step" -> "sqlite3_step stmt" | "borrow-reset" -> "sqlite3_reset stmt" | _ -> "sqlite3_finalize optionalStmt"
                positive.Replace("| Some bytes -> bytes.[0]", "| Some bytes ->\n            let invalidated = " + call + "\n            bytes.[0] + invalidated")
        let path = Harness.project work.Path source
        let checkedProject = Harness.check path
        Assert.True(checkedProject.ParseErrors.IsEmpty, sprintf "%A" checkedProject.ParseErrors)
        let diagnostics = Harness.errors checkedProject
        File.WriteAllText(Harness.at ("test-results/control-" + shape + ".log"), Harness.diagnosticText diagnostics)
        let diagnostic = diagnostics |> List.filter (fun d -> d.Code = code)
        Assert.True(not diagnostic.IsEmpty, sprintf "Expected %s for %s; actual:\n%s" code shape (Harness.diagnosticText (Harness.errors checkedProject)))
        let expectedCascade (d: Diagnostics.Diagnostic) =
            let asyncControl = shape = "unbacked-await" || shape = "borrow-await"
            let samePrimarySite = diagnostic |> List.exists (fun primary -> primary.Range = d.Range)
            d.Code = "CCS8403" &&
            ((asyncControl && d.Message = "Async.RunSynchronously does not activate a settled async formation or orchestration.") ||
             ((asyncControl || samePrimarySite) &&
              d.Message = "Source settlement is required before witnessing: BoundaryEmission: An actual boundary argument's source kind disagrees with the declared formal."))
        let unrelated = Harness.errors checkedProject |> List.filter (fun d -> d.Code <> code && not (expectedCascade d))
        Assert.True(unrelated.IsEmpty, "Control has unrelated errors:\n" + Harness.diagnosticText unrelated)
        for d in diagnostic do
            Assert.Equal(Path.Combine(work.Path, "Main.clef"), d.Range.File)
            Assert.True(d.Range.Start.Line > 0)
        // One end-to-end refusal checks that Compiler/Composer publishes no executable;
        // all eight shapes independently check exact source-bound ProjectChecker errors.
        if shape = "unbacked-await" then
            let exitCode, output = Harness.compile work.Path path
            Assert.NotEqual(0, exitCode)
            Assert.Contains(code, output)
            Assert.False(File.Exists(Path.Combine(work.Path, "probe")), "Refused source published an executable")

    [<Fact>]
    let ``Native static archive executes with ArtifactProof and all memory obligations`` () =
        use work = new Harness.Scratch("native")
        let archive = Harness.archive work.Path
        let path = Harness.project work.Path (Harness.positive ())
        Harness.selectArchive path archive
        let code, output = Harness.compile work.Path path
        Assert.True((code = 0), output)
        let stdout = Harness.succeed work.Path (Path.Combine(work.Path, "probe")) []
        Assert.Equal("sqlite:zero-copy:ok\n", stdout.Replace("\r\n", "\n"))
        let receipts = Directory.GetFiles(work.Path, "artifact-receipt.json", SearchOption.AllDirectories)
        use receipt = Assert.Single receipts |> File.ReadAllText |> JsonDocument.Parse
        Assert.Equal("elf-rocq", receipt.RootElement.GetProperty("Stage").GetString())
        Assert.Equal(Harness.sha (Path.Combine(work.Path, "probe")), receipt.RootElement.GetProperty("ArtifactSha256").GetString().ToLowerInvariant())
        let anchors =
            Seq.append
                (receipt.RootElement.GetProperty("Claims").EnumerateArray() |> Seq.map (fun c -> c.GetProperty("Anchor").GetString()))
                (receipt.RootElement.GetProperty("SolverOnlyAnchors").EnumerateArray() |> Seq.map _.GetString()) |> Set.ofSeq
        let checkedProject = Harness.check path
        Harness.clean checkedProject
        let obligations =
            checkedProject.CheckResult.Graph.Nodes.Values |> Seq.choose (fun node ->
                match node.Kind with SemanticKind.Obligation info -> Some info.Body | _ -> None) |> Seq.toList
        Assert.Contains(obligations, function ObligationBody.ZeroCopyBufferExtent _ -> true | _ -> false)
        Assert.Contains(obligations, function ObligationBody.BorrowedResultViewScope _ -> true | _ -> false)
        let graph = checkedProject.CheckResult.Graph
        let qualified id = graph.Nodes.TryFind id |> Option.bind (Clef.Compiler.PSGSaturation.SemanticGraph.PlatformResolution.qualifiedExportName graph)
        Assert.Contains(obligations, function
            | ObligationBody.ResourceCleanup model ->
                qualified model.Declaration = Some "SQLiteHarness.openConnection" &&
                qualified model.Release = Some "SQLiteHarness.closeConnection"
            | _ -> false)
        let claims =
            checkedProject.CheckResult.Graph.Nodes.Values |> Seq.choose (fun node ->
                match node.Kind with
                | SemanticKind.Obligation info ->
                    match info.Body with
                    | ObligationBody.ZeroCopyBufferExtent _ | ObligationBody.BorrowedResultViewScope _ | ObligationBody.ResourceCleanup _ -> Some info
                    | _ -> None
                | _ -> None) |> Seq.toList
        for claim in claims do Assert.Contains(claim.Id, anchors)
        let discharged = Clef.Compiler.Nanopass.ObligationDischarge.discharge claims |> Harness.accepted
        Assert.Equal(claims.Length, discharged.Evidence.Length)
        for evidence in discharged.Evidence do Assert.Equal("unsat", evidence.Verdict)
        for premise in discharged.Premises do Assert.Equal("sat", premise.Verdict)

module Program =
    [<EntryPoint>]
    let main _ = 0
