using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed class MappingContext
{
    public MappingContext(
        ImmutableArray<ITypeParameterSymbol> methodTypeParameters,
        ImmutableArray<MappingValue> ambientValues,
        MappingMethodConfiguration? configuration = null,
        bool isHelper = false
    )
    {
        MethodTypeParameters = methodTypeParameters;
        AmbientValues = ambientValues;
        Configuration = configuration;
        IsHelper = isHelper;
    }

    public ImmutableArray<ITypeParameterSymbol> MethodTypeParameters { get; }

    public ImmutableArray<MappingValue> AmbientValues { get; }

    public MappingMethodConfiguration? Configuration { get; }

    public bool IsHelper { get; }

    public MappingContext WithAmbient(IEnumerable<MappingValue> values) =>
        new(MethodTypeParameters, values.ToImmutableArray(), Configuration, IsHelper);

    public MappingContext ForHelper() =>
        new(
            MethodTypeParameters,
            AmbientValues.Select((value, index) => new MappingValue(value.Name, value.Type, $"__ambient{index}")).ToImmutableArray(),
            Configuration,
            true
        );
}
