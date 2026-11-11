# Contributing to wenta

wenta is ductwork design in C#: the dependency-free `Wenta.Core` library and the
`WentaZwcad` plugin for ZWCAD 2021 that compiles it in. The plan and the issue map live in
[`zwcad-plugin/ROADMAP.md`](zwcad-plugin/ROADMAP.md) and issue #62.

## Rules for every change

- **All code is C#.** No runtime Python, Mojo, Rust or WASM in the shipped plugin: one
  `Wenta.Core` compiled into one plugin DLL, no FFI.
- **C# never invents math.** A new engineering function is a cited formula or table, then its
  tests in `csharp/Wenta.Core.Tests/Program.cs` (naming the standard or correlation), then the
  implementation. The CSV vectors in `csharp/Wenta.Core.Tests/vectors/` are frozen golden
  fixtures from the retired oracle; do not regenerate or edit them.
- **Keep the build file lists explicit.** A new module goes into `csharp/build.cmd` and
  `zwcad-plugin/build.cmd`; there are no SDK-style projects and no NuGet.
- **Culture-independent output.** Format numbers with the invariant culture; the test machine
  runs a Polish locale.
- **Library half and command half are separate.** An issue that asks for a `WENTA*` command is
  not done until the command exists in `zwcad-plugin/WentaZwcad/` and has been run in ZWCAD;
  a `Wenta.Core` module with tests is the library half only. Say which half a change delivers.
- **Docs describe what exists.** The user guides (`docs/user-guide.en.md`,
  `docs/user-guide.pl.md`) document shipped commands only; keep the EN and PL guides in step.
- Write prose in UK English (the Polish guide in Polish).

## Development

```cmd
just csharp-parity      # build Wenta.Core and run the whole test suite
just zwcad-build        # WentaZwcad.dll + Wenta.CUIX (needs ZWCAD 2021 installed)
just test-all           # everything that runs without ZWCAD
```

Run `just test-all` before you push; `.github/workflows/csharp.yml` runs the same suite on
`windows-latest`.

## Branches, commits and pull requests

- Branch from `main`; land through a pull request, squash-merged.
- Commit subjects use a short area prefix and the issue number where there is one, for example
  `csharp: IFC4 export (#61)`, `tests: ...`, `docs: ...`, `fix: ...`.
- Use `Closes #N` in the pull request body for each issue it finishes, and only for those.

## Closing a session

Before you end a session that changed this repository, whether you are a person or an agent:

1. List every rule, decision or convention the owner gave during the session that is not yet
   written down in this file or in the repository's agent instructions. Examples: naming, paths,
   release steps, tools to avoid.
2. Write each one down in the same change, or open an issue if it needs discussion. A rule that
   exists only in the chat is lost at the next context limit.
3. If work is left half done, put a short dated hand-over note in the pull request description:
   what landed, what ran and what did not, what is still open, and ONE next action.
4. In your final report, say which rules you recorded and where, and which you did not.
