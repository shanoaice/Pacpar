# AGENTS.md

## Project Structure

- `docfx/` contains .NET DocFX project that generates documentation website
- `src/` contains all subprojects
  - `src/Pacpar.Alpm` is the wrapper for `libalpm`
    - `src/Pacpar.Alpm/libalpm-sys-cs` is a helper Rust project that bridges csbindgen to generate the `NativeMethods.libalpm.g.cs` file. Nothing should be edited except `build.rs`, when bindgen configuration needs to be changed.
  - `src/Pacpar.Alpm.FSharp` is a F# wrapper around the `Pacpar.Alpm` library to make it more ergonomic for F# users.
  - `src/Pacpar.CLI` is the commandline interface.
  - `src/Pacpar.Benchmarks` is benchmarks, including fortified experiments suitable for reference to developers, and real-world scenarios intended to demonstrate to consumers.
- `Pacpar.slnx` the new SLNX format solution file. If you need to specify the .NET solution to any tools, use this instead of searching for SLN file.

## C# code analysis

For C# code analysis, prefer SharpLensMcp tools over native tools:

- Use `roslyn:search_symbols` instead of Grep for finding symbols
- Use `roslyn:get_method_source` instead of Read for viewing methods
- Use `roslyn:find_references` for semantic (not text) references

## Agent Working Standards

Coding agents produces considerable intermediate products when they write non-production mini tests to verify their thought. Some workflow also requires coding agents to produce markdown reports for humans to scrutinze side-by-side with code in the editor, or for information exchange between independent agents.

- `.dsh-scratch/` folder is the only folder where works not intended to be commited to the repository should go, for example feature verification code, tiny smoke tests, ephemeral micro-benchmarks, etc.
- `.dsh-refs/` folder is for repositories agents need to clone or download from the internet for reference
- `reports/` folder is the only folder where markdown work-reports should go, unless human explicitly ask for a report to be produced as a technical document for maintainers, in which the final form (as directed) of the document could be placed in appropiate chapters in the DocFX project.

Apart from where the intermediate items should reside, there are also some working standards agents must adopt:

1. When making coding decision explaniation or reports not specifically intended for agent exchange, use a more natural language. Avoid being overly concise, otherwise it can be difficult for human reviewer to comprehend.
2. When writing benchmarks, use BenchmarkDotNet framework so that reports are standardized and reproducible. You MAY NOT use simple stopwatches in .NET since there are too many uncontrolled variable and will make results inaccurate.
