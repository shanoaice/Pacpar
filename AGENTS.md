# AGENTS.md

## Project Structure

- `docfx/` — DocFX sources for the documentation website.
- `src/Pacpar.Alpm` — `libalpm` wrapper. `libalpm-sys-cs/` is a Rust csbindgen helper that generates `NativeMethods.libalpm.g.cs`; edit only its `build.rs`.
- `src/Pacpar.Alpm.FSharp` — F# wrapper that makes `Pacpar.Alpm` ergonomic for F# users.
- `src/Pacpar.CLI` — command-line interface.
- `src/Pacpar.Benchmarks` — BenchmarkDotNet benchmarks: fortified experiments for developers, real-world scenarios for consumers.
- `Pacpar.slnx` — the solution file. Pass this to every tool that asks for one.

## C# Code Analysis

Prefer SharpLensMcp tools over native ones:

- `roslyn:search_symbols` instead of Grep
- `roslyn:get_method_source` instead of Read
- `roslyn:find_references` for semantic references

## Intermediate Outputs

- `.dsh-scratch/` — verification code, smoke tests, micro-benchmarks. Never commit.
- `.dsh-refs/` — repositories cloned or downloaded for reference.
- `reports/` — the only folder for markdown work reports. Exception: when a human asks for a maintainer technical document, place the finished form in the matching DocFX chapter instead.
- Write explanations and reports in readable prose, not terse agent shorthand.

## Testing

- Integration tests manipulate package databases and filesystem trees. Run them only in an isolated container. Never set `PACPAR_ALPM_TEST_ENV=container` on the host.
- On the host, always exclude them: `dotnet test --filter "FullyQualifiedName!~Integration"` (append `-c Release` for Release).
- Use BenchmarkDotNet for all benchmarks. Stopwatch timing is not acceptable.

## Documentation Lifecycle

- `docs/adr/` holds intermediate ADRs: one decision per file, numbered sequentially. They may overlap or contradict in-flight siblings; cross-reference them by number rather than rewriting them.
- When the work stabilizes, revise, compact, and merge the related ADRs into the matching `docfx/` chapter, then supersede or remove the originals. A maintainer should read one coherent account.
- `GLOSSARY.md` is the single source of domain vocabulary. Keep it free of implementation detail and update it as terms settle, not at the end.

## Output Style

- 中文：尽量遵循受控中文技术写作的风格，使用适当长度的句子、主动语态、结论先行、适当分句，避免生硬翻译腔或过于凝练的词汇，禁用空泛词。
- English: Follow ASD-STE100 where possible. Active voice, one instruction per sentence. Use less than 20 words per sentence.
- 代码、命令和 API 名称一律保持原样。觉得冗长时只提建议，不要自行改动。清晰优先，不为简洁牺牲准确性。
