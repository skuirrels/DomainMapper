# Projections

Projection support is an optional provider-neutral surface. Install both packages:

```bash
dotnet add package DomainMapper
dotnet add package DomainMapper.Projections
```

Declare an ordinary create mapping and a projection method that references it:

```csharp
using System.Linq.Expressions;
using DomainMapper.Abstractions;
using DomainMapper.Projections;

[DomainMapper]
public static partial class ContractMapper
{
    [MapMember(nameof(Target.Description), nameof(Source.Detail) + "." + nameof(Detail.Description))]
    public static partial Target Map(Source source);

    [MapProjection(nameof(Map))]
    public static partial Expression<Func<Source, Target>> Project();
}
```

`Project()` returns the same immutable expression instance on every call. The expression contains typed construction, member access, conditional null propagation, and supported conversions; it does not call or compile the in-memory mapper.

Consumers own query creation, filtering, sorting, paging, tracking, materialization, and provider-specific validation. DomainMapper does not hide translation failures or introduce client evaluation. Unsupported mapping operations produce `DMPR106` at build time.

Expression-tree construction requires member metadata and is not supported by DomainMapper under trimming or native AOT. Generated projection accessors carry `RequiresUnreferencedCode` on modern target frameworks so trimmed/AOT publication reports the unsupported use. Compiling an expression can additionally require dynamic code.
