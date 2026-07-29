#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 <baseline-worktree> <candidate-worktree> [BenchmarkDotNet arguments...]" >&2
}

if (( $# < 2 )); then
  usage
  exit 2
fi

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
baseline_root=$(realpath "$1")
candidate_root=$(realpath "$2")
shift 2

dotnet_cmd=${DOTNET_CMD:-dotnet}
dotnet_cmd=$(command -v "$dotnet_cmd")
benchmark_args=(--filter '*RegisterCoverage*' --cli "$dotnet_cmd" "$@")
run_id=$(date -u +%Y%m%dT%H%M%SZ)
receipt_dir="$script_dir/results/$run_id"
artifacts_dir="$script_dir/artifacts/$run_id"

validate_harness_worktree() {
  local harness_status
  harness_status=$(
    git -C "$script_dir" status \
      --porcelain \
      --untracked-files=all \
      -- ':(top)benchmarks/RegisterCoverage' |
      sed '\|^.. benchmarks/RegisterCoverage/results/|d'
  )

  if [[ -n $harness_status ]]; then
    echo "Benchmark harness has uncommitted changes:" >&2
    echo "$harness_status" >&2
    echo "Commit or stash them before creating a receipt." >&2
    exit 1
  fi
}

validate_source_worktree() {
  local label=$1
  local source_root=$2

  git -C "$source_root" rev-parse --is-inside-work-tree >/dev/null
  if [[ -n $(git -C "$source_root" status --porcelain) ]]; then
    echo "$label source worktree is dirty: $source_root" >&2
    exit 1
  fi

  test -f "$source_root/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs"
  test -f "$source_root/src/Stryker.Core/Stryker.Core/InjectedHelpers/Coverage/MutantContext.cs"
}

validate_harness_worktree
validate_source_worktree baseline "$baseline_root"
validate_source_worktree candidate "$candidate_root"

baseline_sha=$(git -C "$baseline_root" rev-parse HEAD)
candidate_sha=$(git -C "$candidate_root" rev-parse HEAD)
harness_sha=$(git -C "$script_dir" rev-parse HEAD)

mkdir -p "$receipt_dir" "$artifacts_dir"

{
  echo "## RegisterCoverage benchmark environment"
  echo
  echo "- UTC run: \`$run_id\`"
  echo "- Harness: \`$harness_sha\`"
  echo "- Baseline: \`$baseline_sha\`"
  echo "- Candidate: \`$candidate_sha\`"
  echo "- Baseline worktree: \`$baseline_root\`"
  echo "- Candidate worktree: \`$candidate_root\`"
  echo "- BenchmarkDotNet arguments:"
  echo
  echo '```text'
  printf '%q' "${benchmark_args[0]}"
  printf ' %q' "${benchmark_args[@]:1}"
  echo
  echo '```'
  echo
  echo '```text'
  "$dotnet_cmd" --info
  echo '```'
} > "$receipt_dir/environment.md"

run_benchmarks() {
  local label=$1
  local source_root=$2
  local label_artifacts="$artifacts_dir/$label"
  local label_receipts="$receipt_dir/$label"

  mkdir -p "$label_artifacts" "$label_receipts"

  STRYKER_SOURCE_ROOT="$source_root" \
    "$dotnet_cmd" clean \
      "$script_dir/RegisterCoverage.Benchmarks.csproj" \
      --configuration Release \
      --nologo

  (
    cd "$script_dir"
    STRYKER_SOURCE_ROOT="$source_root" \
      BENCHMARK_ARTIFACTS="$label_artifacts" \
      "$dotnet_cmd" run \
        --configuration Release \
        --project RegisterCoverage.Benchmarks.csproj \
        -- \
        "${benchmark_args[@]}"
  ) | tee "$label_artifacts/console.log"

  find "$label_artifacts/results" \
    -maxdepth 1 \
    -type f \
    \( -name '*.md' -o -name '*.json' \) \
    -exec cp {} "$label_receipts/" \;

  find "$label_receipts" \
    -maxdepth 1 \
    -type f \
    \( -name '*.md' -o -name '*.json' \) \
    -exec sed -i 's/[[:space:]]*$//' {} +
}

run_benchmarks baseline "$baseline_root"
run_benchmarks candidate "$candidate_root"

echo "Benchmark receipts: $receipt_dir"
