#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
benchmark_project="$repo_root/benchmarks/DomainMap.Benchmarks/DomainMap.Benchmarks.csproj"
pairs="${DOMAINMAP_BENCHMARK_PAIRS:-6}"
job="${DOMAINMAP_BENCHMARK_JOB:-Short}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
result_root="${DOMAINMAP_STABLE_RESULT_ROOT:-/tmp/domainmap-stable-$timestamp}"

if ! [[ "$pairs" =~ ^[1-9][0-9]*$ ]]; then
    echo "DOMAINMAP_BENCHMARK_PAIRS must be a positive integer" >&2
    exit 2
fi

mkdir -p "$result_root"
{
    echo "captured_utc=$timestamp"
    echo "git_commit=$(git -C "$repo_root" rev-parse HEAD)"
    echo "benchmark_pairs=$pairs"
    echo "benchmark_job=$job"
    echo "kernel=$(uname -a)"
    dotnet --version | sed 's/^/dotnet_sdk=/'
} >"$result_root/environment.txt"

dotnet build "$benchmark_project" --configuration Release

generated_root="$repo_root/artifacts/obj/DomainMap.Benchmarks/release/generated"
parity_report="$result_root/DomainMap-vs-Mapperly-parity.json"
dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
    --write-comparison-parity \
    "$generated_root/DomainMap/DomainMap.DomainMapGenerator/DomainMapBenchmarkMapper.g.cs" \
    "$generated_root/Riok.Mapperly/Riok.Mapperly.MapperGenerator/MapperlyBenchmarkMapper.g.cs" \
    "$repo_root/benchmarks/DomainMap.Benchmarks/ComparisonMappingBenchmarks.cs" \
    "$parity_report"

reports=()
for ((pair = 1; pair <= pairs; pair++)); do
    for order in mapperly-first domainmap-first; do
        artifacts="$result_root/runtime/pair-$pair/$order"
        DOMAINMAP_BENCHMARK_ORDER="$order" \
            DOMAINMAP_BENCHMARK_ARTIFACTS="$artifacts" \
            dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
            --exporters json --job "$job" --filter '*ComparisonMappingBenchmarks*'
        reports+=("$artifacts/results/DomainMap.Benchmarks.ComparisonMappingBenchmarks-report-full-compressed.json")
    done
done

minimum_reports=$((pairs * 2))
minimum_samples=$((minimum_reports * 3))
DOMAINMAP_MAPPERLY_PARITY_REPORT="$parity_report" \
    DOMAINMAP_MAPPERLY_FASTER_SCENARIOS='*' \
    DOMAINMAP_MAPPERLY_MIN_REPORTS="$minimum_reports" \
    DOMAINMAP_MAPPERLY_MIN_SAMPLES="$minimum_samples" \
    DOMAINMAP_MAX_MAPPERLY_ALLOCATION_RATIO='1.00' \
    DOMAINMAP_MAPPERLY_ALLOCATION_SLACK_BYTES='0' \
    dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
    --check-comparison "${reports[@]}"

source_artifacts="$result_root/source-generator"
DOMAINMAP_BENCHMARK_ARTIFACTS="$source_artifacts" \
    dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
    --exporters json --filter '*SourceGeneratorBenchmarks*'

source_report="$source_artifacts/results/DomainMap.Benchmarks.SourceGeneratorBenchmarks-report-full-compressed.json"
DOMAINMAP_MAPPERLY_FASTER_SCENARIOS='ColdGeneration' \
    DOMAINMAP_MAPPERLY_MIN_SAMPLES='20' \
    DOMAINMAP_MAX_MAPPERLY_ALLOCATION_RATIO='1.00' \
    DOMAINMAP_MAPPERLY_ALLOCATION_SLACK_BYTES='0' \
    dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
    --check-comparison "$source_report"

echo "Stable benchmark evidence: $result_root"
