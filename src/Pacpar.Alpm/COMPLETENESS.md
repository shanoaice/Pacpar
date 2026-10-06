# libalpm and Pacman API coverage ledger

**Purpose:** the running status of `Pacpar.Alpm` against the native `libalpm` API and the `pacman`
front-end. Numbers and lists here are regenerated after each implementation batch; the reasoning
behind a pick (priority debates, design discussions) belongs in `docs/adr/`, not here.

**Target binding:** `Pacpar.Alpm` (`src/Pacpar.Alpm`)
**Native baseline:** `libalpm` 16.0.1 (pacman 7.0.0). Check the installed build at runtime with
`Alpm.Version` and `Alpm.Capabilities` rather than trusting this number.
**Last recount:** 2026-10-05.

---

## 1. How the numbers were derived

The counts are not hand-maintained. They come from two sets:

1. Every `EntryPoint` string in `bindings/NativeMethods.libalpm.g.cs` — csbindgen emits one
   declaration per function in `alpm.h` and `alpm_list.h`, so this is the whole native surface.
2. Every `NativeMethods.<name>` reference in the high-level sources (everything outside
   `bindings/`). A reference means the wrapper calls it.

To recount:

```bash
python3 - <<'EOF'
import re, pathlib
root = pathlib.Path('src/Pacpar.Alpm')
eps = sorted(set(re.findall(r'EntryPoint\s*=\s*"([^"]+)"',
    (root/'bindings/NativeMethods.libalpm.g.cs').read_text())))
used = set()
for p in root.rglob('*.cs'):
    if 'bindings' in p.parts or 'bin' in p.parts or 'obj' in p.parts: continue
    used |= set(re.findall(r'NativeMethods\.([A-Za-z0-9_]+)', p.read_text()))
bcl = [e for e in eps if e.startswith('alpm_list_')]
cb  = [e for e in eps if re.match(r'alpm_option_get_\w*(cb|cb_ctx)$', e)]
rest = [e for e in eps if e not in bcl and e not in cb]
print(len(eps), 'entry points;', len(bcl), 'list utils;', len(cb), 'callback getters')
print(len([e for e in rest if e in used]), 'wrapped;',
      len([e for e in rest if e not in used]), 'not wrapped')
EOF
```

The `pacman` caller column in section 5 was taken by grepping a pacman checkout; call sites drift
between releases, so treat them as a pointer to read, not as an API contract.

## 2. Coverage overview

| Scope | Total | Wrapped | Not wrapped | Coverage |
| :--- | :---: | :---: | :---: | :---: |
| libalpm entry points | 236 | 236 | 0 | 100% |
| SafeHandle lease overloads (`NativeMethods.SafeHandle.cs`) | 94 | 94 | 0 | 100% |
| High-level API, deliberate omissions excluded | 195 | 164 | 31 | 84.1% |
| Deliberate omission: `alpm_list_*` utilities | 29 | 0 | 29 | — |
| Deliberate omission: `alpm_option_get_*cb*` getters | 12 | 0 | 12 | — |

The P/Invoke layer is generated, so it is complete by construction — the generated declarations are
the whole header surface. The SafeHandle layer is hand-written and grows one overload per function the
high-level API calls with the session handle; it holds the handle alive for the duration of the call.
`SafeBindingPairingTests` pairs each overload against its generated declaration by entry point and
compares arity, parameter types and return type.

## 3. Architecture and deliberate omissions

Three layers, as ADR 0006 sets out:

1. **P/Invoke (`NativeMethods.libalpm.g.cs`)** — generated from `libalpm-sys-cs/build.rs`, `internal`,
   never exposed through the public API.
2. **SafeHandle (`NativeMethods.SafeHandle.cs`)** — hand-written overloads that take
   `SafeAlpmHandle`; the marshaller raises the handle's reference count for the call.
3. **Managed API** — `Alpm`, `Database`, `PackageView`, `Transaction`, `AlpmList<T>`; owns lifetime
   tracking, string conversion and error propagation.

Two groups are omitted on purpose:

- **`alpm_list_*` (29)** — a generic C linked list. .NET collections replace it. `AlpmList<T>` and
  `AlpmStringList` expose native lists without exposing node pointers, and the helpers in
  `AlpmNativeList` are internal.
- **`alpm_option_get_*cb*` (12)** — getters for registered callback pointers and their `void*`
  contexts. Pacman only ever sets callbacks. In C# the callbacks are managed delegates held by
  `Pacpar.Alpm.Events.Callback`; handing out raw function pointers and contexts would break managed
  type safety.

## 4. Status of the APIs selected for implementation

Everything below is implemented and covered by tests. The notes are measured behaviour of libalpm
that the wrapper has to honour — the kind of thing that is easy to re-break and expensive to
rediscover, so it is recorded next to the API it constrains.

| libalpm API | Managed surface | Status | Measured constraint |
| :--- | :--- | :---: | :--- |
| `alpm_version` | `Alpm.Version` | done | Takes no handle; safe before `alpm_initialize`. |
| `alpm_capabilities` | `Alpm.Capabilities` | done | Bitmask; undeclared bits pass through (see `VersionAndCapabilitiesTests`). |
| `alpm_unlock` | `Alpm.Unlock` / `TryUnlock` | done | Releases the lock **this session** holds: `close(lockfd)` then `unlink`. With nothing locked libalpm returns success without touching anything. Stale locks are not its business — the lock is an `open(O_CREAT\|O_EXCL)` file with no holder information, so removing one is a caller decision made with `System.IO.File`. |
| `alpm_logaction` | `Alpm.LogAction` / `TryLogAction` | done | Variadic; the wrapper escapes `%` to `%%` so `vfprintf` consumes no argument. Measured: it does not invoke the registered log callback, contrary to `alpm.h`. |
| `alpm_fetch_pkgurl` | `Alpm.FetchPackageUrls` / `TryFetchPackageUrls` | done | Caller owns the returned strings (`malloc`ed). On failure libalpm frees the list itself and nulls the out parameter, so the wrapper must not free it again. |
| `alpm_checkdeps` | `Alpm.CheckDependencies` | done | Caller owns the `alpm_depmissing_t` list; freed after materialising managed snapshots. |
| `alpm_checkconflicts` | `Alpm.CheckConflicts` | done | Same ownership shape as `alpm_checkdeps`. |
| `alpm_find_satisfier` | `PackageView.FindSatisfier` | done | Returns the caller's own view so it keeps that view's domain. |
| `alpm_find_dbs_satisfier` | `Alpm.FindSatisfier` | done | The owning domain resolves among the caller's `Database` wrappers, never through the handle registry — see `Database.ResolveLifetime`. |
| `alpm_db_update` | `Database.Update`, `Alpm.UpdateDatabases` | done | Invalidate the database's views *before* the call, so views die even when the update fails. Takes and releases the handle lock internally. Documented return 1 ("already up to date") only happens with a custom fetch callback; the curl path returns 0 for "current" as well. Refuses the local database. |
| `alpm_db_search` | `Database.Search` | done | Caller owns the returned list; the package pointers stay with the database. |
| `alpm_db_get_usage` / `alpm_db_set_usage` | `Database.Usage` | done | Values match `alpm.h` (1/2/4/8, all = 15). |
| `alpm_db_add_server`, `alpm_db_remove_server`, `alpm_db_set_servers` | `Database.AddServer`, `RemoveServer`, `SetServers` | done | The bulk setter replaces the list, so it invalidates the database's views. |
| `alpm_db_add_cache_server`, `alpm_db_remove_cache_server`, `alpm_db_set_cache_servers` | `Database.AddCacheServer`, `RemoveCacheServer`, `SetCacheServers` | done | Same invalidation rule. |
| `alpm_pkg_download_size` | `PackageBase.DownloadSize` | done | Triggers libalpm's cache lookup for the package; may return 0 for a package already in the cache. |
| `alpm_pkg_changelog_open`, `_read`, `_close` | `PackageBase.OpenChangelogStream`, `ReadChangelog` | done | The stream has no finalizer: an undisposed one leaks the native archive handle. Dispose it. |
| `alpm_pkg_set_reason` | `PackageView.SetReason`, `InstallReason` | done | Only valid for packages from the local database. |
| `alpm_sync_get_new_version` | `PackageView.GetNewVersion` | done | Searches only the databases passed in, so the owner is always resolvable among them. |
| `alpm_filelist_contains` | `FileList.Contains`, `FileList.FindFile` | done | Native binary search over a sorted list; returns a `PackageFile` snapshot. |
| `alpm_option_get/set_disable_dl_timeout` | `AlpmOptions.DisableDownloadTimeout` | done | Native type is `unsigned short`. |
| `alpm_option_get/set_disable_sandbox_filesystem`, `_network`, `_syscalls` | `AlpmOptions.DisableSandboxFilesystem`, `DisableSandboxNetwork`, `DisableSandboxSyscalls` | done | The native aggregate `alpm_option_get_disable_sandbox` counts only filesystem and syscalls and ignores network; the wrapper's `Sandbox` composite counts all three, so the two can disagree. |
| `alpm_option_get/set_sandboxuser` | `AlpmOptions.SandboxUser` | done | Return 1 means `getpwnam` found no such user, not a malformed string. |

## 5. Not wrapped yet (31)

Priority follows pacman usage and whether a consumer can work around it.

| Function | pacman caller | Tier | Recommendation |
| :--- | :--- | :--- | :--- |
| `alpm_pkg_mtree_open`, `alpm_pkg_mtree_next`, `alpm_pkg_mtree_close` | `src/pacman/check.c` (`check_pkg_full`) | High | The manifest of what a package was supposed to install: mode, uid/gid, size, mtime, symlink target and sha256 per file — everything `alpm_pkg_get_files()` does not carry, and the only baseline `pacman -Qkk` can compare an installed tree against. Constraints to honour: it reads `<dbpath>/local/<pkg>/mtree` and returns NULL when that file is absent, and every other backend (sync databases, file-loaded packages) returns NULL, so the API is local-database-only; `archive_entry` is borrowed and dies on the next `next`, so entries must be copied like `PackageFile`; the archive handle needs the same owned-handle treatment the changelog stream is still missing. |
| `alpm_pkg_check_pgp_signature`, `alpm_db_check_pgp_signature`, `alpm_siglist_cleanup` | `src/pacman/package.c` | Medium | Signature verification. Needs a managed `SignatureList` that owns the cleanup. |
| `alpm_decode_signature`, `alpm_extract_keyid` | `src/pacman/package.c` | Medium | Signature decoding and key extraction. |
| `alpm_dep_from_string`, `alpm_dep_compute_string`, `alpm_dep_free` | `src/pacman/conf.c`, `src/pacman/callback.c` | Medium | `Depend.Parse` / `ToString`; freeing stays with the managed wrapper. |
| `alpm_option_set_architectures`, `alpm_option_set_cachedirs`, `alpm_option_set_ignorepkgs`, `alpm_option_set_ignoregroups`, `alpm_option_set_noextracts`, `alpm_option_set_noupgrades`, `alpm_option_set_overwrite_files`, `alpm_option_set_hookdirs`, `alpm_option_set_assumeinstalled` | `src/pacman/conf.c` | Medium | Bulk list setters. The collections already expose `Add`/`Remove`; these replace the whole list in one native call. |
| `alpm_sandbox_setup_child` | `src/pacman/conf.c` | Medium | Privilege drop in a child process; only useful if the wrapper ever spawns hook runners. |
| `alpm_option_match_noextract`, `alpm_option_match_noupgrade` | `src/pacman/check.c` | Low | Pattern matching; keep native semantics rather than reimplementing globbing. |
| `alpm_pkg_get_xdata` | `src/pacman/package.c` | Low | Extended package metadata. |
| `alpm_compute_md5sum`, `alpm_compute_sha256sum` | `src/pacman/package.c`, `src/pacman/check.c` | Low | Use `System.Security.Cryptography`; MD5 is only needed for legacy checksums. |
| `alpm_pkg_find` | `src/pacman/sync.c` | Low | Covered by LINQ. |
| `alpm_option_get/set_disable_sandbox` | none | Low | The aggregate getter/setter. Adding it alongside `DisableSandboxNetwork` would make the two meanings collide; decide before adding. |
| `alpm_option_get_physical_architectures`, `alpm_pkg_get_installed_db` | none | — | Not called by pacman. Assess with a concrete consumer need. |

## 6. What pacman implements outside libalpm

A consumer building a package manager on `Pacpar.Alpm` has to provide these itself:

- **Configuration parsing** (`pacman.conf`, `Include`, repo sections) — libalpm has no parser;
  configure the handle imperatively.
- **Signal handling and rollback** — trap `SIGINT`/`SIGTERM`, call `Transaction.Interrupt()`, then
  release the lock. In C# the equivalent is `Console.CancelKeyPress` plus `Alpm.Unlock()` on the way
  out.
- **Terminal UI** — download progress, transaction progress, interactive questions.
- **Command orchestration** — target resolution, group expansion, conflict resolution, download
  sizing, confirmation, commit.

## 7. Suggested order for the remaining work

1. MTREE traversal: the only High-tier item left, and the one `pacman -Qkk` cannot do without.
2. Bulk option setters: cheap, and they remove the most repetitive call patterns.
3. PGP family: needs an ownership story for the signature list first.
4. Everything else on demand, with a consumer that needs it.
