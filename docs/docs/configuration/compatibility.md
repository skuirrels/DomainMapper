# Compatibility matrix

| Surface                       | Supported lanes                                                                                                            |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| Consumer target frameworks    | .NET Framework 4.8, .NET 8, .NET 9, .NET 10                                                                                |
| SDK/compiler hosts            | .NET SDK 8, 9, and 10 with their supported C# language versions                                                            |
| Roslyn analyzer hosts         | 4.8, 4.11, 4.14, and 5.0                                                                                                   |
| Runtime abstractions          | `netstandard2.0`                                                                                                           |
| Optional projection contracts | `netstandard2.0`; BCL expression trees only                                                                                |
| Trimming/native AOT           | Direct mapping, registry dispatch, and reference tracking are validated on .NET 10; projections are explicitly unsupported |

The package selects a versioned analyzer build deterministically for the active Roslyn host. Clean package consumers validate analyzer loading and generated-code compilation in every target-framework lane. Dropping a lane is a compatibility change governed by semantic versioning.

Generated mapping and registry methods use no runtime reflection metadata. Projection expressions are immutable and safe to retrieve concurrently in untrimmed applications, but expression construction requires member metadata and generated accessors carry `RequiresUnreferencedCode` where available.
