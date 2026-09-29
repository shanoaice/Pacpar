# Speculative Integration Test Plan: Real-World Libalpm & Arch Linux Repository Workflows

> **Document Status**: Architectural Specification & Proposal  
> **Target Path**: `reports/speculative-integration-tests.md`  
> **Related Components**: `Pacpar.Alpm`, `containers/alpm-tests`, `src/Pacpar.Alpm.Tests`

---

## 1. Executive Summary & Purpose

During the test suite audit of `Pacpar.Alpm`, existing integration tests (`Integration/AlpmInitializationTests.cs` and `Integration/NativeMethodsSmokeTests.cs`) were identified as redundant with unit tests and purged. They performed zero privileged operations, verified no system state, and tested no package interactions that could not be run hermetically on a host machine using temporary directory redirection (`IsolatedAlpmEnvironment`).

However, **genuine integration testing remains essential for libalpm wrappers**. Libalpm is not merely a file parser; it is a full package manager engine whose real-world correctness depends on:
1. Complex dependency graph resolution (SAT solving across thousands of packages).
2. PGP signature verification and trust chains using GnuPG.
3. System file extraction, filesystem permissions, and file conflict resolution.
4. Shell scriptlet execution (`.INSTALL`) and ALPM trigger hooks.
5. Network synchronization with official Arch Linux mirrors.

This document outlines the **speculative, recommended integration tests** that should be implemented inside isolated container environments (using `containers/alpm-tests/Containerfile`). These tests are deliberately **not implemented yet**, serving as an architectural blueprint for future milestones.

---

## 2. Boundary Matrix: Unit vs. Temp-Redirected vs. Containerized

To maintain fast developer feedback loops and absolute host safety, tests must be partitioned according to their execution requirements:

| Test Scope | Target Environment | Host Safety | Execution Speed | Typical Targets |
| :--- | :--- | :--- | :--- | :--- |
| **Pure Unit** | Host (`dotnet test`) | 100% Safe (In-Memory) | Sub-millisecond | Enums, string marshalling, IL GC-anchor audits, reflection, lifetime tokens. |
| **Temp Directory Redirect** | Host (`IsolatedAlpmEnvironment`) | 100% Safe (Local `/tmp`) | ~1–5 ms | Empty DB queries, option properties, synthetic offline packages (`PackageArchive.Create`), transaction lifecycle & error mapping without scriptlets. |
| **Containerized Integration** | OCI Container (`podman`/`docker`) | Isolated sandbox | Seconds (I/O & Network) | Real Arch repository sync, PGP keyring verification, package install scriptlets (`/bin/sh`), ALPM trigger hooks, sandboxed downloaders. |

---

## 3. Speculative Integration Test Scenarios

### Scenario 1: Sync Database Synchronization & PGP Signature Verification

- **Objective**: Verify that `Pacpar.Alpm` correctly synchronizes remote sync databases (`core.db.tar.gz`, `extra.db.tar.gz`), exposes server lists, and enforces PGP signature verification policies.
- **Why Container is Required**:
  - GnuPG operations require an initialized, populated pacman keyring (`/etc/pacman.d/gnupg`).
  - Corrupting or modifying keyring trust levels on a host machine can break host package management.
- **Test Workflow**:
  1. Container boots with Arch Linux base and populated `archlinux-keyring`.
  2. Configure an official Arch mirror (or a local container-internal HTTP server serving captured snapshot databases).
  3. Register `core` and `extra` sync databases with `SigLevel = ALPM_SIG_DATABASE_ALWAYS`.
  4. Invoke database sync/update.
  5. **Positive Case**: Ensure `database.GetPackageCache()` loads valid package entries.
  6. **Negative Case (Tampered DB)**: Provide a database tarball with an invalid or mismatched `.sig` file. Assert that libalpm fails the transaction and surfaces a typed `AlpmSignatureException` (with `ALPM_ERR_DB_INVALID_SIG` or `ALPM_ERR_SIG_MISSING`).
- **Key Assertions**:
  - `alpm.Errno` surfaces appropriate signature error codes.
  - No orphaned GPG lockfiles remain.
  - Download and signature callbacks receive expected notifications.

---

### Scenario 2: Large-Scale Dependency Solving on Real Arch Linux Graphs

- **Objective**: Validate dependency solving, virtual packages, soname provisions, and version constraint matching against real-world package repositories containing 15,000+ packages.
- **Why Synthetic Tests Fall Short**:
  - Synthetic test packages typically test 1–3 packages with trivial dependencies (`glibc`).
  - Real Arch repositories feature complex cycles, circular opt-depends, split packages, virtual provides (e.g., `libcurl.so=4-64`, `sh`, `cron`, `awk`), and multi-layer version comparisons (`>= 2.1-3`).
- **Test Workflow**:
  1. Load synchronized `core` and `extra` sync databases from snapshot fixtures.
  2. Initiate transaction to install complex packages (e.g., `git`, `neovim`, or `plasma-desktop`).
  3. Run `transaction.Prepare()`.
  4. Inspect the resulting transaction package list (`transaction.GetAdd()`): verify that every transitive dependency is correctly staged in topological dependency order.
  5. Test conflict scenarios: attempt to co-install mutually conflicting packages (e.g., packages providing identical binaries without `replaces`). Assert `AlpmTransactionException` carries the structured conflict pair.
- **Key Assertions**:
  - Dependency order matches native `alpm_trans_prepare` output.
  - Conflict descriptions accurately identify both packages and the conflicting file/dependency.
  - Native heap and list structures are cleanly released when transactions are rolled back or disposed.

---

### Scenario 3: End-to-End Package Installation & Scriptlet Execution (`.INSTALL`)

- **Objective**: Test transaction commit, archive extraction, MTREE integrity verification, and bash scriptlet invocation (`pre_install`, `post_install`, `post_upgrade`).
- **Why Container is Required**:
  - Package scriptlets invoke `/bin/sh` with root permissions to configure system users, run `systemd-sysusers`, create system directories, or execute shell commands.
  - Executing real scriptlets on the host would modify host system configuration.
- **Test Workflow**:
  1. In container, construct a custom `.pkg.tar.zst` containing:
     - Real binary files targeted at `/usr/bin/` and `/etc/`.
     - An `.INSTALL` scriptlet defining `post_install()` that writes a sentinel file to `/tmp/scriptlet-executed`.
  2. Begin transaction, add package, prepare, and commit.
  3. Listen to `AlpmEvent` callback: verify `ScriptletInfo` events stream output from the executing bash scriptlet.
  4. Assert the package is committed to the local database (`/var/lib/pacman/local/<pkg>-<version>/`).
  5. Assert the sentinel file `/tmp/scriptlet-executed` exists with expected contents.
  6. Assert installed files exist in the root with correct permissions and ownership.
- **Key Assertions**:
  - Real-time logging from scriptlet stdout/stderr flows through managed callbacks.
  - Local database registers package files and `.INSTALL` contents.
  - Interrupted or failed scriptlet aborts/reports appropriately without crashing the CLR process.

---

### Scenario 4: ALPM Trigger Hook Lifecycle (`.hook`)

- **Objective**: Verify that libalpm hooks in `/usr/share/libalpm/hooks/` and `/etc/pacman.d/hooks/` are detected, triggered by file target matching, and executed during transaction commit.
- **Why Container is Required**:
  - Hooks run host utilities (`glib-compile-schemas`, `update-desktop-database`, `ldconfig`, etc.).
- **Test Workflow**:
  1. In container, place a test hook in `/etc/pacman.d/hooks/99-test.hook`:
     - `Type = Path`, `Target = usr/share/pacpar-test/*`.
     - `When = PostTransaction`, `Exec = /usr/bin/touch /tmp/hook-executed`.
  2. Commit a package that installs a file under `usr/share/pacpar-test/sample.txt`.
  3. Verify events `AlpmEvent.HookRunStart` and `AlpmEvent.HookStart` fire with `HookWhen.PostTransaction`.
  4. Verify `/tmp/hook-executed` is created.
  5. Commit a package that does *not* touch `usr/share/pacpar-test/`. Verify the hook does not execute.
- **Key Assertions**:
  - `HookRunStart` accurately reports total hooks to run and current hook index.
  - Managed enum `HookWhen` aligns with native event payload.

---

### Scenario 5: File Conflict, Backup Preservation (`.pacnew` / `.pacsave`), and Overwrite Flags

- **Objective**: Validate libalpm's configuration file preservation logic and file conflict handling during upgrades and removals.
- **Test Workflow**:
  1. Install version 1.0 of a package declaring `/etc/testpkg.conf` in its `backup` list.
  2. Simulate user modification: write custom text into `/etc/testpkg.conf`.
  3. Upgrade to version 2.0 with an updated default `/etc/testpkg.conf`.
  4. **Verify `.pacnew` Generation**:
     - User's modified `/etc/testpkg.conf` remains unchanged.
     - New configuration is written to `/etc/testpkg.conf.pacnew`.
     - `AlpmEvent.PacnewCreated` event is emitted.
  5. Remove package: verify `/etc/testpkg.conf` is saved as `/etc/testpkg.conf.pacsave`.
- **Key Assertions**:
  - Events report exact paths for `.pacnew` and `.pacsave` files.
  - Filesystem contents match ALPM backup rules.

---

### Scenario 6: Unprivileged Sandboxed Downloader (`SandboxUser`)

- **Objective**: Verify `Alpm.Options.SandboxUser` drops root privileges to an unprivileged user (e.g. `nobody` or `pacman`) when performing downloads.
- **Why Container is Required**:
  - Requires Linux capability `CAP_SETUID` and `CAP_SETGID` to drop privileges, and system users defined in `/etc/passwd`.
- **Test Workflow**:
  1. In container, set `Alpm.Options.SandboxUser = "nobody"`.
  2. Initiate download of a remote package or database URL.
  3. Intercept download process or examine downloaded file ownership in cache directory.
  4. Verify that child processes spawned by libalpm run under the UID of `nobody`.
- **Key Assertions**:
  - Setting invalid user throws or sets errno `ALPM_ERR_WRONG_ARGS`.
  - Downloads succeed and temporary cache files are readable by the main process.

---

## 4. Test Infrastructure Recommendations

1. **OCI Test Container Environment**:
   - Base image: `archlinux:base-devel`.
   - Pre-installed assets:
     - `libalpm` (native development headers and binaries).
     - Initialized pacman keyring (`pacman-key --init && pacman-key --populate archlinux`).
     - .NET SDK (installed via official pacman or dotnet-install script).
   - Execution command: `podman run --privileged --rm -v $(pwd):/workspace:Z archlinux-pacpar-tests dotnet test --filter "Category=ContainerIntegration"`.

2. **Mock Mirror / Local HTTP Server**:
   - To avoid flakiness and external network dependencies, container test suites should embed a lightweight mock HTTP server (or utilize a read-only container directory served by `nginx`/`darkhttpd`/kestrel).
   - Snapshot repository databases (`core.db.tar.gz`) from an Arch Linux archive date should be bundled in `.dsh-scratch/test-repos/` or container volumes.

3. **Trait Filtering**:
   - All genuine container integration tests must carry `[Trait("Category", "ContainerIntegration")]`.
   - Standard host CI (`dotnet test`) will continue running all 150+ unit and hermetic temp-redirected tests with zero container dependencies in under 100 milliseconds.
