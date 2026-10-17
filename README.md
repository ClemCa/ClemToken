# ClemToken

ClemToken is a grammar-driven parser for .NET. You describe a language in a `.ct`
grammar, compile it to a compact `.cta` file, and parse text from the command line or
from C#.

It also exposes:

- parser-driven syntax tokens as JSON
- static grammar analysis (rule counts and symbol bounds)
- runtime parse metrics (allocations, time, per-rule attribution, call tree)

## Requirements

- .NET SDK 10.0 or newer (the project targets `net10.0`).

## Build

```bash
dotnet build -c Release
```

Run from source:

```bash
dotnet run -c Release -- --version
```

The built executable is under `bin/Release/net10.0/`:

- Windows: `ClemToken.exe`
- Linux/macOS: `ClemToken`

The examples below use `ClemToken` for brevity.

### Publish

Self-contained, single-file binaries:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o bin/win-x64
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o bin/linux-x64
```

Framework-dependent build:

```bash
dotnet publish -c Release --self-contained false -o bin/light
```

A library DLL:

```bash
dotnet publish -c Release -r win-x64 -p:OutputType=Library -o bin/library
```

On Windows, `build.bat` automates all of the above and publishes self-contained and
framework-dependent binaries plus library DLLs into `bin/`:

- `ClemToken-windows.exe` / `ClemToken-windows-light.exe`
- `ClemToken-linux` / `ClemToken-linux-light`
- `ClemToken-library-windows.dll` / `ClemToken-library-linux.dll`

## Quick start

The repository contains `test_grammar.ct` (a small language) and `test_code.txt` (sample
input).

```bash
# Compile the grammar once.
ClemToken build test_grammar.ct -o test_grammar.cta

# Parse with the compiled grammar.
ClemToken parse test_grammar.cta test_code.txt
```

`parse` also accepts a `.ct` grammar directly and compiles it in memory:

```bash
ClemToken parse test_grammar.ct test_code.txt
```

Use `-` for stdin:

```bash
cat test_code.txt | ClemToken parse test_grammar.cta -
```

PowerShell:

```powershell
Get-Content test_code.txt | ClemToken parse test_grammar.cta -
```

Or pass literal text with `--text`:

```bash
ClemToken parse test_grammar.ct --text "var x = 1;"
```

## CLI

### Common options

| Option | Meaning |
| --- | --- |
| `-h`, `--help` | Show help. Also works after a command: `ClemToken parse --help`. |
| `--verbose` | Write diagnostics to stderr. Normal output stays on stdout. |
| `--log <path>` | Write diagnostics to a file. |
| `--` | Stop parsing options. Useful for paths that start with `-`. |

### Commands

| Command | Purpose |
| --- | --- |
| `build` | Compile a `.ct` grammar into a `.cta` file. |
| `parse` | Parse input and print the parse tree. |
| `tokens` | Print syntax tokens as JSON. |
| `analyze` | Static analysis of a grammar. |
| `profile` | Runtime metrics for a parse. |
| `test` | Run the built-in golden tests. |
| `help` | Show help for a command. |
| `version` | Show the version and grammar format. |

### `build`

```text
ClemToken build <grammar.ct> [-o <out.cta>]
ClemToken build <grammar.ct> --display
ClemToken build <grammar.ct> --check
```

- `-o`, `--output <path>` — output file. Defaults to `<grammar>.cta`; `-` writes the
  compiled bytes to stdout.
- `--display` — print the lowered rules instead of writing a file.
- `--check` — validate the grammar and exit. Useful in CI.
- `--log <path>` — write generator diagnostics to a file.
- `--verbose` — write diagnostics to stderr.

`build` expects grammar source (`.ct`), not a compiled grammar. When the grammar comes
from stdin, `-o`, `--display`, or `--check` is required.

```bash
ClemToken build dsl.ct -o bin/dsl.cta
ClemToken build dsl.ct --display
ClemToken build dsl.ct --check
cat dsl.ct | ClemToken build - -o bin/dsl.cta
```

### `parse`

```text
ClemToken parse <grammar> <input|-> [options]
ClemToken parse <grammar> --text <text> [options]
```

- `grammar` — a `.ct` source or a compiled `.cta`.
- `input` — an input file, or `-` for stdin.
- `-t`, `--text <text>` — parse literal text instead of a file.
- `-f`, `--format <tree|json>` — output format. Default: `tree`.
- `--allow-errors` — exit 0 even when the tree contains `Error` nodes.
- `--log <path>` — write parser diagnostics to a file.

The recovered tree is printed even when parsing fails. Errors are written to stderr as:

```text
input.txt:3:7: error: unexpected input '...'
```

By default `parse` exits 1 if the tree contains any `Error` nodes. Use
`--allow-errors` when a caller wants to inspect the recovery tree without treating it
as a failure.

```bash
ClemToken parse test_grammar.cta test_code.txt
ClemToken parse test_grammar.cta test_code.txt --format json
ClemToken parse test_grammar.ct --text "import a from b;"
cat test_code.txt | ClemToken parse test_grammar.cta -
```

### `tokens`

```text
ClemToken tokens <grammar> <input|-> [options]
ClemToken tokens <grammar> --text <text> [options]
```

Prints the parser-driven syntax token stream as JSON (`clemtoken.tokens`). The output
uses the same recovery parser as `parse`, so it is also produced for incomplete or
invalid input. `tokens` exits 0 even when recovery tokens are present.

```bash
ClemToken tokens test_grammar.cta test_code.txt
ClemToken tokens test_grammar.ct --text "import a from b;"
cat test_code.txt | ClemToken tokens test_grammar.cta -
```

### `analyze`

```text
ClemToken analyze <grammar> [options]
```

Static analysis of a grammar: rule/spec counts, construct counts, and per-rule symbol
bounds. No input is parsed.

- `grammar` — a `.ct` source, a compiled `.cta`, or `-` for grammar source on stdin.
- `--json` — emit `clemtoken.grammarStats` JSON instead of the text table.

```bash
ClemToken analyze dsl.ct
ClemToken analyze dsl.ct --json
cat dsl.ct | ClemToken analyze - --json
```

### `profile`

```text
ClemToken profile <grammar> <input|-> [options]
ClemToken profile <grammar> --text <text> [options]
```

Runtime metrics for a parse: elapsed time, allocated bytes, per-rule execution/match/
failure counts, and inclusive/self attribution.

- `--json` — emit `clemtoken.parseMetrics` JSON.
- `--tree` — emit `clemtoken.parseTree` JSON, which includes the merged call tree used
  for flame graphs. `--tree` implies JSON even without `--json`.

```bash
ClemToken profile test_grammar.cta test_code.txt
ClemToken profile test_grammar.cta test_code.txt --json
ClemToken profile test_grammar.cta test_code.txt --tree
```

### `test`

```text
ClemToken test generator <file>
ClemToken test parser <file> <grammar.ct>
```

Runs the golden tests. The files are plain text pairs of an input (or grammar) and an
expected tree; see `test_generator.txt` and `test_parser.txt` for the format. The
command exits 1 if any case fails.

```bash
ClemToken test generator test_generator.txt
ClemToken test parser test_parser.txt test_grammar.ct
```

`test.bat` is an interactive Windows menu for the same tests, plus the
writing/reading/full-pipeline checks.

### `help` and `version`

```bash
ClemToken help
ClemToken help parse
ClemToken version
ClemToken --version
```

`--version` prints both the CLI version and the compiled-grammar format:

```text
ClemToken 1.8.0 (grammar format 13)
```

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success. |
| `1` | Runtime failure: missing file, invalid grammar, invalid compiled grammar, `parse` found `Error` nodes, or a golden test failed. |
| `2` | Usage error: unknown command, unknown option, wrong argument count, conflicting options. |

### Input conventions

- A grammar path ending in `.ct` is compiled in memory. Any other grammar path is
  treated as a compiled grammar (normally `.cta`).
- `-` means stdin.
- Grammar and input cannot both come from stdin.
- `--text` cannot be combined with an input file.
- Console input/output are pinned to UTF-8 so source offsets and token values survive
  pipes.
- Compiled `.cta` files carry a format version. If a newer CLI reports a format
  mismatch, rebuild the grammar with `ClemToken build`.

## Library

ClemToken can be referenced as a normal .NET project. Referencing an `Exe` project is
supported:

```xml
<ItemGroup>
  <ProjectReference Include="..\ClemToken\ClemToken.csproj" />
</ItemGroup>
```

Alternatively, reference one of the library DLLs produced by `build.bat`.

The parser API used here is in the `ClemToken` namespace:

```csharp
using ClemToken;
```

### Compile a grammar

```csharp
const string grammar = """
    # A tiny expression grammar.
    $number: [0-9]+

    @sum: @number (@operator @number)*
    @operator: [+ | - | * | /]
    @number: $[number]
    """;

grammar.ToGrammar("calc.cta");
```

`ToGrammar` compiles the grammar text and writes the compiled bytes to the path you
provide. The extension is just a convention; use `.cta`. `ToGrammar(BinaryWriter)`
writes to a stream instead.

This is the library equivalent of `ClemToken build`.

The snippets below build on that example: `grammar` is the grammar text and `source` is
the input text.

### Parse and navigate

```csharp
const string source = "1 + 2 * 3";
Navigator root = source.ParseWith("calc.cta");

if (root.GoTo("sum"))
{
    root.First();
    do
    {
        Token child = root.Current();
        Console.WriteLine($"{child.Name} = {child.Value}");
    } while (root.Next());
}

foreach (Token token in root.Flatten())
{
    Console.WriteLine($"{token.Name} = {token.Value} at {token.Line}:{token.Column}");
}
```

`ParseWith` expects a compiled grammar. Compile the `.ct` first with `ToGrammar` or the
CLI's `build` command. `Flatten()` always walks from the parse root.

`Navigator` is a cursor over the tree. The important detail is that `Current()` returns
the selected **child**, not the node the cursor is on; `CurrentTree()` returns that
child's tree. `Is(name)` tells you which node the cursor is on.

### Syntax tokens

```csharp
string json = source.TokensJson("calc.cta");
```

The result is the same `clemtoken.tokens` contract as `ClemToken tokens`.

### Static grammar analysis

```csharp
GrammarStats stats = grammar.AnalyzeGrammar();

Console.WriteLine($"{stats.RuleCount} rules, {stats.SpecCount} specs");
Console.WriteLine($"total symbols: {stats.Total}");
Console.WriteLine(stats.ToJson());
```

To read the grammar from a file:

```csharp
// When the grammar is on disk:
GrammarStats stats = "calc.ct".AnalyzeGrammarFile();
```

`GrammarStats` exposes `RuleCount`, `SpecCount`, the construct counts (`SimpleCount`,
`ReservedCount`, `GroupCount`, `ReferenceCount`, `AnchorCount`, `OptionalCount`,
`LookaheadCount`, `EmptyCount`, `TotalNodes`), `Rules`, and `Total`.

### Runtime profiling

```csharp
ParseMetrics metrics = source.MeasureParse("calc.cta");

Console.WriteLine($"{metrics.TotalAllocatedBytes} bytes");
Console.WriteLine($"{metrics.TotalElapsed.TotalMilliseconds:0.###} ms");
Console.WriteLine(metrics.ToJson());
Console.WriteLine(metrics.ToTreeJson());
```

`MeasureParse` expects a compiled grammar. `ToTreeJson()` emits `clemtoken.parseTree`,
including the merged call tree. `ParseMetrics.Rules` contains the per-rule metrics, and
`ParseMetrics.Tree` contains the call-tree nodes.

### Preloading

```csharp
"calc.cta".PreloadAsGrammar();

Navigator root = source.Parse();
string tokens = source.TokensJson();
ParseMetrics metrics = source.MeasurePreloaded();
```

`PreloadAsGrammar` stores the grammar in process-wide static state. Load once and reuse
it. The parser also keeps a few static tuning values, so parsing with different grammars
concurrently in the same process is not safe. Use separate processes or serialize those
parses.

### API summary

| Call | Purpose |
| --- | --- |
| `grammar.ToGrammar(path)` | Compile grammar text and write a `.cta`. |
| `grammar.ToGrammar(BinaryWriter)` | Compile grammar text to a stream. |
| `path.PreloadAsGrammar()` | Load a compiled grammar into process-wide state. |
| `text.ParseWith(grammarPath)` | Parse text with a compiled `.cta`. |
| `text.ParseWith(BinaryReader)` | Parse text with a compiled grammar from a stream. |
| `text.Parse()` | Parse with the preloaded grammar. |
| `text.TokensJson(grammarPath)` | Syntax tokens JSON from a compiled `.cta`. |
| `text.TokensJson()` | Syntax tokens JSON from the preloaded grammar. |
| `grammar.AnalyzeGrammar()` | Static analysis of grammar text. |
| `path.AnalyzeGrammarFile()` | Static analysis of a `.ct` file. |
| `text.MeasureParse(grammarPath)` | Runtime metrics with a compiled `.cta`. |
| `text.MeasureParse(BinaryReader)` | Runtime metrics with a compiled grammar stream. |
| `text.MeasurePreloaded()` | Runtime metrics with the preloaded grammar. |

### `Navigator`

| Member | Meaning |
| --- | --- |
| `Children` | Number of children of the current node. |
| `Is(name)` / `Is(name, value)` | Check the current node's name/value. |
| `Has(name)` | Check for a direct child with that name. |
| `GoTo(name)` | Descend into a direct child by name. |
| `Enter()` | Descend into the currently selected child. |
| `Back(count = 1)` | Move up one or more levels. |
| `First()` / `Last()` | Select the first/last child. |
| `Next()` / `Previous()` | Move the child selection. |
| `GoToRelative(offset)` | Descend into a child by offset from the selection. |
| `GoToNext(name)` | Descend into the first child with that name at or after the selection. |
| `PreviousSibling()` / `NextSibling()` | Move to a sibling node. |
| `IsFirst()` / `IsLast()` | Check the child selection. |
| `NextIs(name, value)` | Check the next child. |
| `HasLeft(...)` / `HasPassed(...)` | Look ahead/behind in the child list. |
| `Current()` | Token of the selected child, or the current node's token for a leaf. |
| `CurrentTree()` | Tree of the selected child. |
| `Flatten(leavesOnly = true, includeIgnored = false)` | Flatten the parse root into tokens. |

### `Token`

| Field | Meaning |
| --- | --- |
| `Name` | Grammar rule or spec name. |
| `Value` | Matched source text. |
| `Line`, `Column` | 1-based start position. |
| `EndLine`, `EndColumn` | 1-based exclusive end position. |
| `Start`, `End` | Absolute UTF-16 offsets into the source. |
| `Ignored` | Whether the token is hidden from normal traversal. |

`Line`/`Column` are resolved by the parser. `Start`/`End` are valid for the token's
whole lifetime.

## Grammar format

A `.ct` file is line-oriented. Each non-empty line is a spec, a rule, or a comment.

```text
# A tiny expression grammar.
$number: [0-9]+

@sum: @number (@operator @number)*
@operator: [+ | - | * | /]
@number: $[number]
```

### Specs

```text
$name: [a-zA-Z_][a-zA-Z0-9_]*
$keyword:simple: if
$spacing: horizontal
```

- `$name: pattern` defines a spec using a regular expression (the default mode).
- `$name:simple: text` defines a literal/simple matcher.
- `$spacing:` sets whitespace handling: `implicit` (default), `explicit` (do not skip),
  or `horizontal` (skip spaces/tabs but treat line breaks as significant).
- `$comment:`, `$comment_begin:`, `$comment_end:`, and `$comment_escape:` describe
  comments. Comment content is replaced with spaces so source offsets are preserved.

### Rules

```text
@sum: @number (@operator @number)*
```

- `@name:` starts a rule (an anchor).
- Bare text is a literal/simple token.
- `@name` references another rule.
- `$[name]` matches a spec as a named reserved token; `$$[name]` matches it as an
  ignored reserved token.
- `( ... )` groups elements.
- `[a | b]` is an optional group with alternatives.
- Lookahead: `@>name`, or `@>>[a | b]`.
- Repeat suffixes: `?`, `*`, `+`, `N`, `N-M`.
- A trailing `:` marks a group ignored; `_` is a no-op modifier.
- Spacing modifiers: `!` (explicit) and `$explicit:`/`$implicit:`.

The full grammar language is easiest to learn from the grammars in this repository:

- `dsl.ct` — the grammar for the DSL used by the editor extension
- `test_grammar.ct` — the grammar used by the golden tests
- `dsl_sample.ct` — a minimal example

## JSON contracts

All JSON is pretty-printed. Each payload carries a `schema` and a `version`.

| Schema | Command | Notes |
| --- | --- | --- |
| `clemtoken.tree` | `parse --format json` | Parse tree with positions. |
| `clemtoken.tokens` | `tokens` | Flat syntax-token stream. |
| `clemtoken.grammarStats` | `analyze --json` | Static rule/spec analysis. |
| `clemtoken.parseMetrics` | `profile --json` | Runtime metrics. |
| `clemtoken.parseTree` | `profile --tree` | Runtime metrics plus merged call tree. |

`clemtoken.tree` positions are 0-based and LSP-shaped. `endLine`/`endCharacter` are
exclusive; `start`/`end` are absolute UTF-16 offsets.

`clemtoken.tokens` example:

```json
{
  "schema": "clemtoken.tokens",
  "version": 1,
  "tokens": [
    { "rule": "number", "leaf": "number", "value": "1", "line": 0, "character": 0, "length": 1 },
    { "rule": "operator", "leaf": "+", "value": "+", "line": 0, "character": 2, "length": 1 }
  ]
}
```

In `clemtoken.tokens`, `rule` is the nearest enclosing grammar rule (the highlight
taxonomy), `leaf` is the terminal's own rule/spec name, and positions are 0-based.
`length` is in UTF-16 units.

`clemtoken.grammarStats` reports `low`/`high` symbol bounds per rule; `high` is `null`
for an unbounded rule. `clemtoken.parseMetrics` reports allocations in bytes and times
in milliseconds. `clemtoken.parseTree` adds a flat `tree` array where each node points
at its parent and at an entry in `rules`.

## Repository layout

| Path | Contents |
| --- | --- |
| `CLI.cs` | Command-line parsing and command implementations. |
| `Program.cs` | Public `Utils` library API and the process entry point. |
| `Generator.cs` | `.ct` grammar compiler. |
| `ParserV3.cs` | Parser, recovery, and spacing/lookahead handling. |
| `Serialization.cs` | `.cta` binary format. |
| `Navigator.cs` | Tree cursor API. |
| `Metrics.cs` | Grammar analysis and runtime metrics. |
| `SyntaxTokens.cs` | `clemtoken.tokens` contract. |
| `Tester.cs` | Golden-test runner. |
| `dsl.ct` | Grammar for the DSL used by the editor extension. |
| `test_grammar.ct` | Grammar used by the golden tests. |

## License

GLWTS. See [LICENSE](LICENSE).
