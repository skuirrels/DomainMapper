#!/usr/bin/env bash

set -Eeuo pipefail

# pack a nupkg for each Roslyn version supported by DomainMapper
# and merge them together into one nupkg

roslyn_versions=('4.8' '4.11' '4.14' '5.0')

RELEASE_VERSION=${RELEASE_VERSION:-"1.2.0-dev.$(date +%s)"}
RELEASE_NOTES=${RELEASE_NOTES:-''}

# https://stackoverflow.com/a/246128/3302887
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &>/dev/null && pwd)
artifacts_dir="${script_dir}/../artifacts"

echo "building DomainMapper v${RELEASE_VERSION} for ${DOMAINMAPPER_ENVIRONMENT:-'local'}"
echo "cleaning artifacts dir"
mkdir -p "${artifacts_dir}"
rm -rf "${artifacts_dir:?}"/*

artifacts_dir="$(realpath "$artifacts_dir")"
source_generator_path="$(realpath "${script_dir}/../src/DomainMapper")"
projections_path="$(realpath "${script_dir}/../src/DomainMapper.Projections")"

for roslyn_version in "${roslyn_versions[@]}"; do
    echo "building for Roslyn ${roslyn_version}"
    dotnet pack \
        "$source_generator_path" \
        --verbosity quiet \
        -c Release \
        /p:HUSKY=0 \
        /p:ROSLYN_VERSION="${roslyn_version}" \
        -o "${artifacts_dir}/roslyn-${roslyn_version}" \
        /p:Version="${RELEASE_VERSION}" \
        /p:PackageReleaseNotes=\""${RELEASE_NOTES}"\"
done

echo "merging multi targets to a single nupkg"
zipmerge "${artifacts_dir}/DomainMapper.${RELEASE_VERSION}.nupkg" "${artifacts_dir}"/*/*.nupkg
dotnet pack \
    "$projections_path" \
    --verbosity quiet \
    -c Release \
    /p:HUSKY=0 \
    -o "${artifacts_dir}" \
    /p:Version="${RELEASE_VERSION}" \
    /p:PackageReleaseNotes=\""${RELEASE_NOTES}"\"
echo "built ${artifacts_dir}/DomainMapper.${RELEASE_VERSION}.nupkg"
echo "built ${artifacts_dir}/DomainMapper.Projections.${RELEASE_VERSION}.nupkg"
