# Contributing

We would love for you to contribute to DomainMap.

Start with a focused issue that describes the domain boundary, expected generated code, diagnostics, and compatibility impact. Pull requests should include generator tests, runtime tests when behavior is observable, and benchmark coverage for performance-sensitive changes.

Domain invariants belong in domain constructors, factories, or methods. The generator should bind and call those APIs, not duplicate business rules.
