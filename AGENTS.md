# AGENTS.md

## Project Structure

- `docfx/` contains .NET DocFX project that generates documentation website
- `src/` contains all subprojects
  - `src/Pacpar.Alpm` is the wrapper for `libalpm`
    - `src/Pacpar.Alpm/libalpm-sys-cs` is a helper Rust project that bridges csbindgen to generate the `NativeMethods.libalpm.g.cs` file. Nothing should be edited except `build.rs`, when bindgen configuration needs to be changed.
  - `src/Pacpar.Alpm.FSharp` is a F# wrapper around the `Pacpar.Alpm` library to make it more ergonomic for F# users.
  - `src/Pacpar.CLI` is the commandline interface.
- `Pacpar.slnx` the new SLNX format solution file. If you need to specify the .NET solution to any tools, use this instead of searching for SLN file.

## C# code analysis

For C# code analysis, prefer SharpLensMcp tools over native tools:

- Use `roslyn:search_symbols` instead of Grep for finding symbols
- Use `roslyn:get_method_source` instead of Read for viewing methods
- Use `roslyn:find_references` for semantic (not text) references
