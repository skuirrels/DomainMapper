#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
benchmark_project="$repo_root/benchmarks/DomainMapper.Benchmarks/DomainMapper.Benchmarks.csproj"
pairs="${DOMAINMAPPER_BENCHMARK_PAIRS:-6}"
source_pairs="${DOMAINMAPPER_SOURCE_BENCHMARK_PAIRS:-2}"
job="${DOMAINMAPPER_BENCHMARK_JOB:-Short}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
result_root="${DOMAINMAPPER_STABLE_RESULT_ROOT:-/tmp/domainmapper-stable-$timestamp}"

if ! [[ "$pairs" =~ ^[1-9][0-9]*$ ]]; then
    echo "DOMAINMAPPER_BENCHMARK_PAIRS must be a positive integer" >&2
    exit 2
fi

if ! [[ "$source_pairs" =~ ^[1-9][0-9]*$ ]]; then
    echo "DOMAINMAPPER_SOURCE_BENCHMARK_PAIRS must be a positive integer" >&2
    exit 2
fi

mkdir -p "$result_root"
{
    echo "captured_utc=$timestamp"
    echo "git_commit=$(git -C "$repo_root" rev-parse HEAD)"
    echo "benchmark_pairs=$pairs"
    echo "source_benchmark_pairs=$source_pairs"
    echo "benchmark_job=$job"
    echo "kernel=$(uname -a)"
    dotnet --version | sed 's/^/dotnet_sdk=/'
} >"$result_root/environment.txt"

dotnet build "$benchmark_project" --configuration Release

generated_root="$repo_root/artifacts/obj/DomainMapper.Benchmarks/release/generated"
parity_report="$result_root/DomainMapper-vs-Mapperly-parity.json"
domainmapper_generated=(
    "$generated_root/DomainMapper/DomainMapper.DomainMapperGenerator/"*DomainMapperBenchmarkMapper*.g.cs
)
if [[ ${#domainmapper_generated[@]} -ne 1 || ! -f "${domainmapper_generated[0]}" ]]; then
    echo "Expected exactly one generated DomainMapper benchmark source" >&2
    exit 2
fi
dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
    --write-comparison-parity \
    "${domainmapper_generated[0]}" \
    "$generated_root/Riok.Mapperly/Riok.Mapperly.MapperGenerator/MapperlyBenchmarkMapper.g.cs" \
    "$repo_root/benchmarks/DomainMapper.Benchmarks/ComparisonMappingBenchmarks.cs" \
    "$parity_report"

reports=()
for ((pair = 1; pair <= pairs; pair++)); do
    for order in mapperly-first domainmapper-first; do
        artifacts="$result_root/runtime/pair-$pair/$order"
        DOMAINMAPPER_BENCHMARK_ORDER="$order" \
            DOMAINMAPPER_BENCHMARK_ARTIFACTS="$artifacts" \
            dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
            --exporters json --job "$job" --filter '*ComparisonMappingBenchmarks*'
        reports+=("$artifacts/results/DomainMapper.Benchmarks.ComparisonMappingBenchmarks-report-full-compressed.json")
    done
done

minimum_reports=$((pairs * 2))
minimum_samples=$((minimum_reports * 3))
DOMAINMAPPER_MAPPERLY_PARITY_REPORT="$parity_report" \
    DOMAINMAPPER_MAPPERLY_FASTER_SCENARIOS='*' \
    DOMAINMAPPER_MAPPERLY_MIN_REPORTS="$minimum_reports" \
    DOMAINMAPPER_MAPPERLY_MIN_SAMPLES="$minimum_samples" \
    DOMAINMAPPER_MAX_MAPPERLY_ALLOCATION_RATIO='1.00' \
    DOMAINMAPPER_MAPPERLY_ALLOCATION_SLACK_BYTES='0' \
    dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
    --check-comparison "${reports[@]}"

source_reports=()
for ((pair = 1; pair <= source_pairs; pair++)); do
    for order in mapperly-first domainmapper-first; do
        source_artifacts="$result_root/source-generator/pair-$pair/$order"
        DOMAINMAPPER_BENCHMARK_ORDER="$order" \
            DOMAINMAPPER_BENCHMARK_ARTIFACTS="$source_artifacts" \
            dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
            --exporters json --filter '*SourceGeneratorBenchmarks*'
        source_reports+=("$source_artifacts/results/DomainMapper.Benchmarks.SourceGeneratorBenchmarks-report-full-compressed.json")
    done
done

source_minimum_reports=$((source_pairs * 2))
DOMAINMAPPER_MAPPERLY_FASTER_SCENARIOS='ColdGeneration' \
    DOMAINMAPPER_MAPPERLY_MIN_REPORTS="$source_minimum_reports" \
    DOMAINMAPPER_MAPPERLY_MIN_SAMPLES='40' \
    DOMAINMAPPER_MAX_MAPPERLY_ALLOCATION_RATIO='1.00' \
    DOMAINMAPPER_MAPPERLY_ALLOCATION_SLACK_BYTES='0' \
    dotnet run --no-build --configuration Release --project "$benchmark_project" -- \
    --check-comparison "${source_reports[@]}"

echo "Stable benchmark evidence: $result_root"
