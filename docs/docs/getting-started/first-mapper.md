# First mapper

Reference the `DomainMapper` package, mark a partial class with `[DomainMapper]`, and declare a partial method:

```csharp
using DomainMapper.Abstractions;

[DomainMapper]
public static partial class CustomerMapper
{
    public static partial CustomerView ToView(Customer source);
}
```

DomainMapper matches readable source properties to constructor parameters or writable target properties by name, ignoring case. Unsupported construction paths produce a compile-time diagnostic.
