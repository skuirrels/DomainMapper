---
sidebar_position: 0
description: A guide on how to contribute to DomainMap.
---

# Contributing

DomainMap welcomes carefully reviewed contributions, whether written manually or with AI assistance. The contributor remains responsible for understanding every submitted line, preserving the architecture, and proving behavior with tests.

Read the repository's `CODE_OF_CONDUCT.md` and `CONTRIBUTING.md` before starting.

## Before changing code

1. Describe the domain or mapping problem with a minimal example.
2. Search existing DomainMap issues and pull requests in the repository where this source is published.
3. For a major feature, discuss the design before implementation.
4. Read the [architecture](./architecture.md) and [testing](./tests.md) guides.

## Pull-request expectations

- Keep changes focused and preserve source compatibility unless a breaking change is intentional.
- Add generator tests for emitted source and diagnostics.
- Add integration tests when runtime behavior or domain invariants matter.
- Add or update benchmarks for changes on a hot mapping or generator path.
- Run formatting, the full test suite, and relevant benchmarks locally.
- State which generated code you inspected and which edge cases you exercised.

Low-quality generated changes that the submitter cannot explain or verify should not be merged.

## Local verification

```bash
dotnet csharpier check .
dotnet build DomainMap.slnx -p:HUSKY=0
dotnet test DomainMap.slnx -p:HUSKY=0 --no-build --no-restore
```

See `docs/benchmarks.md` in the repository root for the comparison methodology.
