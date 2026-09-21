#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
runtime="${PACPAR_CONTAINER_RUNTIME:-}"
image="${PACPAR_ALPM_TEST_IMAGE:-pacpar-alpm-tests}"

if [[ -z "$runtime" ]]; then
  for candidate in podman nerdctl docker; do
    if command -v "$candidate" >/dev/null 2>&1; then
      runtime="$candidate"
      break
    fi
  done
fi

if [[ -z "$runtime" ]]; then
  printf 'No supported container runtime found. Set PACPAR_CONTAINER_RUNTIME to podman, nerdctl, or docker.\n' >&2
  exit 1
fi

"$runtime" build -f "$repo_root/containers/alpm-tests/Containerfile" -t "$image" "$repo_root"
"$runtime" run --rm -t -v "$repo_root:/workspace:Z" -w /workspace "$image" dotnet test Pacpar.slnx --filter Category=Integration
