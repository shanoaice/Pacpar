# Pacpar.CLI Considerations: a pacman-shaped CLI with a RazorConsole UI

> **Date:** October 2026
> **Scope:** What to consider building in `Pacpar.CLI`, and what the underlying layers
> (`Pacpar.Alpm`, `Pacpar.Alpm.FSharp`, `RazorConsole.Elmish`) should guarantee so those
> affordances are reachable when the CLI is built.
> **Evidence base:** oma 1.28.0's interface (analysed in the oma repo at
> `docs/arch-driver-ui-analysis.md`), Arch ecosystem research from primary sources
> (ArchWiki, pacman(8), yay/paru docs, AUR RPC — fetched 2026-10-15), the
> RazorConsole.Elmish / RazorConsole.Core 0.6.0 capability survey, and this repository's
> current binding state.

---

## 1. Executive summary

Keep the CLI grammar strictly pacman-shaped — `pacpar <operation> [options] [targets]`
with pacman's operations (`-S`, `-R`, `-Q`, `-U`, `-F`, `-D`, `-T`) and pacman's option
letters with pacman's semantics. Everything that makes the tool friendlier than pacman
should live *inside* the flows that grammar already has, not beside it as a second
interface.

The evidence says the friendly layer should be "enhanced CLI", not "TUI application".
oma, the most thorough recent attempt at a friendly package-manager interface, is exactly
that: plain verbs with layered upgrades (rich review surface, pickers, pagers) and hard
fallbacks whenever the terminal can't support them. On the Arch side, adoption data is
blunt: line-mode helpers yay (2,650 AUR votes) and paru (1,260) dwarf every purpose-built
TUI (pacseek 55, pacsea 5, yup 8), and even pacseek delegates installs to a CLI helper
([AUR RPC](https://aur.archlinux.org/rpc/v5/info?arg[]=yay&arg[]=paru&arg[]=pamac-aur&arg[]=oma),
[pacseek](https://github.com/moson-mo/pacseek)). What Arch users demonstrably reward is
good prompts and good review tooling inside a pacman-shaped command.

Three interaction slots are worth TUI-ish treatment, and RazorConsole.Core 0.6.0 already
has the widgets for all three:

1. **The transaction review surface** — replace `:: Proceed with installation? [Y/n]`
   with a scrollable preview (partitioned tables, colored version diffs, size deltas) and
   in-surface confirm. This is oma's strongest idea and maps directly onto the F# layer's
   existing `PreparedTransaction` / `TransactionSummary` design
   (`src/Pacpar.Alpm.FSharp/DESIGN.md:27-77`).
2. **Pickers** — provider/group/version selection rendered as `Select<T>` menus instead of
   bare numbered prompts, while still accepting pacman's numeric answers.
3. **The AUR review queue** — the one place where "TUI-ish" is not decoration but the
   product: Arch policy *requires* PKGBUILD review before sourcing
   ([AUR wiki](https://wiki.archlinux.org/title/Arch_User_Repository#Verify_the_PKGBUILD)),
   and no existing helper packages that duty as a single guided surface.

Everything must degrade to pacman-style line mode when output is piped, `--noconfirm` is
passed, or stdin is not a terminal. Machine-readable output (`--print`/`--print-format`)
stays pacman-compatible.

---

## 2. Why this shape, briefly

**From oma** (full analysis in the oma repo, `docs/arch-driver-ui-analysis.md`):

- The interface is a progressively enhanced CLI, not a TUI app. Tables and pickers appear
  only on a real TTY (`oma-pager/src/pager.rs:32-33`); piping the pending-review surface
  without `--yes` is refused outright (`src/table.rs:398-404`); non-interactive mode is
  derived (`!is_terminal() || --yes || CI`, `src/install_progress.rs:108-114`).
- The single best-regarded affordance is the pre-transaction review: partitioned
  tables (removed / installed / upgraded / downgraded / reinstalled), red/green version
  diffs, per-package size deltas, a floating summary with totals, and confirm as a
  keystroke inside the same surface (`src/table.rs:520-695`, `oma-pager/src/key_binding.rs:111-141`).
- The full-screen "app store" browser exists (bare `oma`), but it is the least essential
  layer for a driver whose users are command-line natives.

**From Arch:** pacman has no rich preview (only `VerbosePkgLists`), no undo, no news
([pacman(8)](https://man.archlinux.org/man/pacman.8.en)); helpers fill the gaps but the
wiki grades them on prompt *content* — file review before sourcing, diff views, reliable
parsers, no unsafe flags — not on UI form
([AUR helpers](https://wiki.archlinux.org/title/AUR_helpers)). yay/paru already cover the
"workflow" conveniences (combined upgrades, news, devel tracking, answer presets), so a
new driver wins on interaction quality, correctness, and the review experience — not on
feature checklists.

---

## 3. The CLI shape to preserve

### 3.1 Grammar

```bash
pacpar <operation> [options] [targets]
```

Operations follow pacman: `-S` (sync), `-R` (remove), `-Q` (query), `-U` (upgrade
file/URL — this is also where built AUR artifacts land), `-F` (files), `-D` (database),
`-T` (deptest), `-V`, `-h`. No new verbs for the friendly features.

Helper-specific extras fit the established paru precedent: an auxiliary operation for
things that touch no database (paru's `-P` carries `--news`, `--stats`, `--complete`,
`--gendb`), and long-only flags elsewhere. Consider `-P`-shaped homes for anything the
grammar can't express — including, later, history/undo (see §7).

### 3.2 Option compatibility

Match pacman's letters and semantics; the review surface changes *how* a confirmation
looks, never *whether* a flag means what it means in pacman. Candidates worth committing
to day one (the scripting surface is ecosystem law, per pacman(8)'s own `--noconfirm`
guidance):

| Flag | Keep as | Note |
| --- | --- | --- |
| `--noconfirm` | identical | bypasses the review surface entirely (auto-confirm) |
| `-y` / `-u` / `-s` / `-i` / `-l` / `-o` / `-c` / `-d` / `-w` | identical | core sync/query verbs |
| `-q`, `--noprogressbar` | identical | quiet/noisy-output control for scripts |
| `--print`, `--print-format` | identical + optional new tokens | the machine-readable preview; also the line-mode twin of the review surface |
| `--asdeps`, `--asexplicit`, `--needed` | identical | install reasons (`ALPM_TRANS_FLAG_ALLDEPS`/`ALLEXPLICIT`) |
| `--dbpath`, `--root`, `--config`, `--logfile`, `--cachedir` | identical | non-negotiable for testing/containers |
| `--overwrite`, `--assume-installed`, `-dd`, `--ignore` | decide subset | audit counts `-Sy`/`--ask`-style footguns as "unsafe flags"; be deliberate about which dangerous ones exist at all |

Two parity details that are easy to lose in a TUI refresh:

- **Prompt answerability.** Scripts answer pacman's numbered prompts by piping input
  ("1\n"). Pickers may look different, but must still accept the same numeric answers on
  stdin, and `--noconfirm` must make them deterministic (pacman picks defaults; match its
  choices).
- **Exit codes.** Keep pacman-ish semantics (0 success, 1 failure, 130 SIGINT); the F#
  layer's stable failure codes (`docs/adr/0002-stable-failure-codes-for-fsharp.md`) are the
  natural mapping source.

### 3.3 One open decision: bare invocation

pacman prints usage on bare invocation; yay/paru run `-Syu`. This is a positioning choice:
if Pacpar.CLI is a pacman replacement, print usage (shape purity); if it is positioned as
a helper, bare = full upgrade matches user expectation. The oma choice — bare launches a
full TUI — is the one to *not* copy here; it contradicts both conventions.

---

## 4. Where the TUI-ish value lands

All three surfaces below are things pacman *already does in worse form* (a y/N prompt, a
numbered list, nothing), so the grammar never changes — only the rendering of an existing
interaction point.

### 4.1 Transaction review surface (the centerpiece)

Replace `:: Proceed with installation? [Y/n]` with a review surface:

- Partitioned tables: removed / installed / upgraded / downgraded / reinstalled, with
  old→new version diffs colored per segment, per-package size deltas, and a summary line
  (total download size, net disk change). oma's diff presentation (`src/table.rs:520-695`)
  is the reference; the data comes free from `Transaction.GetAddedPackages()` /
  `GetRemovedPackages()` (`src/Pacpar.Alpm/Transaction.cs:263-287`) plus local-DB
  correlation for old versions/sizes.
- RazorConsole composition: `Panel` (title "Pending changes") + `SpectreTable` sections +
  `Markup` styled spans for diffs + `ViewHeightScrollable` for long transactions + a bottom
  confirm bar. `ModalWindow` is reserved for danger-class confirmations (see 4.4).
- **Inline viewport preferred over alternate screen** for this surface, keeping the shell
  scrollback visible (the oma history browser uses the same trick,
  `Viewport::Inline(24)`, `src/subcommand/history.rs:45-50`). RazorConsole.Core supports
  inline mode when the alternate screen is disabled
  (`ConsoleLiveDisplayContext` incremental line updates, RazorConsole.Core 0.6.0 XML docs).
- **Degradation rule (copy oma's):** not a TTY or `--noconfirm` → pacman-style text
  report and a normal y/N (or auto-yes). Piping without `--noconfirm` → still show the
  text report and read the answer from stdin, exactly like pacman. Never require a TUI to
  complete a transaction.

The model for this surface already exists in the F# design: `PreparedTransaction` exposes
a pure `TransactionSummary` while holding `db.lck`, then user confirmation, then
`Transaction.commit` (`src/Pacpar.Alpm.FSharp/DESIGN.md:27-77`). The review surface *is*
the UI for that phase — worth keeping the summary type wide enough to render it (see §5.2).

### 4.2 Pickers

Provider selection (the `AlpmQuestion.SelectProvider` callback,
`src/Pacpar.Alpm/Events/AlpmQuestion.cs`), group selection, and multi-version selection
are today bare numbered prompts in pacman. Render them as `Select<T>` menus
(RazorConsole.Core `Select<T>`: focusable list, keyboard navigation, formatter), and
extend to `oma pick`-style version browsing when both repo and archive versions exist.

- `Select<T>` is single-select; pacman's group prompt is multi-select. Either compose a
  multi-select from `Rows` + selection state in the Elmish model, or add a `MultiSelect<T>`
  to the widget gap list (§5.3).
- Numeric answers must remain accepted (§3.2). The picker is a rendering upgrade, not a
  new protocol.

### 4.3 AUR review queue (the differentiator)

When a transaction includes AUR packages, the pre-commit phase should walk each package's
review obligations before anything is sourced or built:

- Per-package panel: PKGBUILD, `.install`, `.patch` files; `git show`-style diff against
  the previously installed build (paru/yay already do diffs; the win is presenting them as
  one continuous review queue instead of scattered prompts).
- RazorConsole.Core `SyntaxHighlighter` (ColorCode, line numbers, themes) fits PKGBUILD
  and diff rendering directly; `Markdown` covers AUR metadata/comments display.
- Hard rules from Arch policy: nothing is `source`d/executed before review; `.SRCINFO`/
  AUR RPC are the metadata source (the wiki's "reliable parser" criterion); `--noconfirm`
  on AUR packages should still require an explicit review-skip opt-in (paru's
  `--skipreview` precedent) rather than silently building.
- Degradation: line-mode prints the files and diffs to stdout and waits for a per-package
  y/n — the yay/paru behavior — when there's no TTY.

### 4.4 Danger confirmations

oma's "Do as I say!" pattern (type the phrase to remove essential packages,
`src/subcommand/remove.rs:260-294`) is worth a direct equivalent for `-R` of base/system
packages and for `--overwrite`-class operations. `ModalWindow` + `TextInput` cover this;
the line-mode fallback is the same typed phrase via stdin.

### 4.5 Progress and log streaming

- The F# event architecture is already shaped for this: `EventSink.toChannel` splits
  critical lifecycle events (unbounded) from progress (bounded, conflating, capacity-1)
  (`src/Pacpar.Alpm.FSharp/DESIGN.md:162-235`). That conflating channel is exactly the
  backpressure a terminal renderer needs.
- RazorConsole.Core has **no progress/multi-progress widget** (only `BarChart` and
  `Spinner`). Options: compose progress from styled `Markup` bars inside
  `ViewHeightScrollable`, or plan a `ProgressBar`/`MultiProgress` component. Decide this
  before the CLI stage; the download UI is otherwise hand-rolled.
- Respect the renderer's cost: ~110–130 μs and ~220 KB per snapshot; coalesce updates to
  a 16–33 ms tick (RazorConsole.Elmish `docs/research/implementation_guide.md:270-278`).
  The F# conflating channel gives this for free — don't bypass it with per-callback
  dispatch.
- Install-phase log output (dpkg-analog: libalpm hook/scriptlet output) streams as
  appended lines above the pinned progress bar — oma's bottom-bar trick
  (`src/install_progress.rs:35-90`); with RazorConsole this is a `Rows` of log lines +
  a status row, updated at the coalesced tick.

### 4.6 Query output

Keep pacman behavior: `-Q*`/`-Ss`/`-Si`/`-Fl` print to stdout. A scrollable pager may be
offered when stdout is a TTY and output is long (oma pages `search`/`show`/`files`), but
stdout-first keeps the shape honest. `--print-format` remains the machine interface.

---

## 5. What the earlier layers should guarantee

These are the contracts that decide whether §4 is cheap or expensive when the CLI is
built.

### 5.1 `Pacpar.Alpm` (C# binding)

The survey confirms the hard parts are done: transactions
(`src/Pacpar.Alpm/Transaction.cs:20-335`), callbacks including question-answer mutation
(`src/Pacpar.Alpm/Events/Callback.cs:115-440`, `AlpmQuestion.cs:89-296`), packages and
file lists. The gaps that directly gate the CLI's flows (fuller list in the repo survey
and `reports/repo-audit-report-cycle1.md`):

1. `alpm_db_update` — generated (`NativeMethods.libalpm.g.cs:509`) but unwrapped; `pacman -Sy` is impossible without it.
2. `alpm_db_add_server`/`set_servers` — mirror setup (`Pending/DatabaseServerTests.cs`).
3. `alpm_find_satisfier` / `alpm_db_search` — target resolution and `-s` search currently require LINQ scans.
4. **pacman.conf parsing** — not in libalpm at all; a managed parser (sections, servers, SigLevel, cache/hook dirs) must exist before the CLI can behave like pacman (`--config` honored).
5. `alpm_unlock`, `alpm_checkdeps`, `alpm_checkconflicts`, `alpm_sync_get_new_version`, public `vercmp` — all generated, all unwrapped (`Pending/`).
6. `alpm_pkg_get_db` on `PackageView` — the review surface shows `extra/ripgrep`-style origins; without this the CLI must track origins by hand.
7. Defect **L-06** (`AlpmEvent.cs:114-115, 132-133`) — null native package pointers in `PackageOperation*` events will SIGSEGV the very event stream the progress UI subscribes to. This is on the UI's critical path.

### 5.2 `Pacpar.Alpm.FSharp` (F# surface)

- Keep `TransactionSummary` rich enough to render §4.1 without re-querying libalpm:
  operation kind per package, old/new version strings, download size, installed-size
  delta, origin database name, and (later) an AUR/repo tag. It is the view model.
- Keep the event structs (`[<Struct>] ProgressInfo` etc.) and the split channel design as
  the UI feed contract; the CLI should consume `EventSink.toChannel`, not raw callbacks.
- Stable failure codes (`docs/adr/0002-...`) become exit codes and error rendering; per
  the glossary, surface "ALPM operation failure" with its "Failure detail" (dep lists,
  conflicts — already snapshotted in `AlpmTransactionException`), and keep "Query miss" a
  non-failure.

### 5.3 `RazorConsole.Elmish`

"Fully maps RazorConsole.Core" is the right bar; for the CLI specifically, the mapping
should land these first because §4 is built from them: `ViewHeightScrollable`,
`Scrollable`, `Select<T>`, `TextInput`, `SpectreTable` family, `ModalWindow`, `Markup`,
`Panel`/`Rows`/`Columns`/`FlexBox`. Three things the mapping will *not* provide and that
someone must own (Elmish layer or CLI layer — decide where):

1. **A keybinding/mode layer.** The engine has no shortcut router or mode machine
   (vim-style modes must live in Elmish `Model`/`Update`). The review surface needs
   y/n/q/`Ctrl-C` and `/` search-key semantics.
2. **Multi-select** (§4.2) — `Select<T>` is single-select only.
3. **Progress rendering** (§4.5) — no progress bar component in the engine.

Also bake in the renderer cost contract (coalescing/throttling) as documented guidance or
a helper; every streaming surface in §4 depends on it.

---

## 6. AUR helper function

None of this lives in libalpm; it is a layer beside it. The pieces (from the repo survey,
§6c):

1. **AUR RPC v5 client** — `info`/`search` metadata: versions, deps, votes, out-of-date flags.
2. **`.SRCINFO` parser** — via `makepkg --printsrcinfo`; split packages, arch constraints,
   `depends`/`makedepends`/`checkdepends`.
3. **Mixed dependency resolver** — one graph over sync-DB caches plus AUR results, with
   topological build order.
4. **Git cache** — clone/fetch `https://aur.archlinux.org/<pkg>.git`, retain the previous
   build's tree for diff review (§4.3).
5. **Privilege-separated `makepkg` runner** — unprivileged build (wiki requirement),
   escalation only for the final `pacman -U` equivalent (i.e. `Transaction.AddPackage(loaded)` +
   `Commit`), `--sudoloop`-style behavior for long builds.
6. **Artifact ingestion** — `Alpm.LoadPackage` (`src/Pacpar.Alpm/Alpm.cs:208`) +
   `Transaction.AddPackage` (`Transaction.cs:174`) are already bound; a batch-ingest
   helper for split packages completes the path.

Flag-policy constraints from the wiki ("unsafe flags" column): no `-Sy` partial upgrades
(`-Syu` combined only), no `--ask`/`--nodeps`-style pacman-fooling. If the AUR layer
offers skipping repo updates in a combined upgrade menu (paru does), warn loudly —
partial upgrades are the canonical "defective system" failure mode
([System maintenance](https://wiki.archlinux.org/title/System_maintenance#Partial_upgrades_are_unsupported)).

Devel/-git package tracking (`--devel` in yay/paru) is worth a design slot early even if
implemented late: it needs a state file and touches the `-Qu` code path.

---

## 7. Consider deferring

- **Full-screen package browser.** Demand data (§1) says line-mode wins; if built at all,
  make it opt-in via an auxiliary operation, never the bare-invocation default.
- **History/undo.** The genuine ecosystem gap — libalpm has no revert, and "undo" on Arch
  means reinstalling recorded versions from retained archives. The F# `TransactionReport`
  is the natural ledger record; a `-P`-shaped `history`/`undo` extension is the
  pacman-shape-compatible home. But archive/PKGBUILD retention policy makes this heavy —
  phase 2 of the CLI at the earliest.
- **Terminal theme probing.** oma's termbg latency dance (`src/color.rs:17-139`) is
  polish; `NO_COLOR` + Spectre defaults are enough initially. (Spectre respects
  `NO_COLOR`; the engine has no background-detection.)
- **OSC progress/bell niceties.** oma emits OSC 94 tab progress (`src/install_progress.rs:126-170`);
  RazorConsole.Core's `AnsiSequences` doesn't wrap OSC 94 or bells. Nice, never essential.

---

## 8. Open questions

1. **Bare invocation** (§3.3): usage like pacman, or `-Syu` like yay/paru?
2. **Auxiliary operation vs long-only flags** for helper extras: adopt paru's `-P`, or
   stay even stricter to pacman's option set?
3. **Dangerous-flag subset** (§3.2 table): which of `--overwrite`, `-dd`,
   `--assume-installed`, `--ignore` exist day one, and which are deliberately omitted?
4. **Widget ownership** (§5.3): do multi-select and progress live in RazorConsole.Elmish's
   layer, or as CLI-local components? (Engine-level would benefit every future
   RazorConsole app; CLI-local ships sooner.)
5. **AUR review granularity**: per-package y/n queue vs. one combined review document —
   cheap to try both behind the same model.

---

### Appendix: evidence index

- oma analysis: oma repo `docs/arch-driver-ui-analysis.md` (this workspace's oma checkout).
- oma source facts cited inline (`src/table.rs`, `src/install_progress.rs`, `oma-pager/`, etc.).
- Arch: [pacman(8)](https://man.archlinux.org/man/pacman.8.en),
  [AUR](https://wiki.archlinux.org/title/Arch_User_Repository),
  [AUR helpers](https://wiki.archlinux.org/title/AUR_helpers),
  [System maintenance](https://wiki.archlinux.org/title/System_maintenance),
  [libalpm_trans(3)](https://man.archlinux.org/man/libalpm_trans.3.en),
  [yay](https://github.com/Jguer/yay/blob/next/doc/yay.8),
  [paru](https://github.com/Morganamilo/paru/blob/master/man/paru.8),
  [AUR RPC](https://aur.archlinux.org/rpc/v5/info?arg[]=yay&arg[]=paru&arg[]=pamac-aur&arg[]=oma).
- RazorConsole: `RazorConsole.Elmish` repo `docs/research/elmish_razorconsole_architecture.md`,
  `docs/research/implementation_guide.md`; widget facts from RazorConsole.Core 0.6.0 XML docs
  (NuGet package).
- Pacpar: cited inline (`src/Pacpar.Alpm/…`, `src/Pacpar.Alpm.FSharp/DESIGN.md`,
  `src/Pacpar.Alpm.Tests/Pending/`, `reports/repo-audit-report-cycle1.md`).
