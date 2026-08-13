using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DomainMapper.Engine;

internal sealed class MappingContext
{
    public MappingContext(ImmutableArray<ITypeParameterSymbol> methodTypeParameters, ImmutableArray<MappingValue> ambientValues)
    {
        MethodTypeParameters = methodTypeParameters;
        AmbientValues = ambientValues;
    }

    public ImmutableArray<ITypeParameterSymbol> MethodTypeParameters { get; }

    public ImmutableArray<MappingValue> AmbientValues { get; }

    public MappingContext WithAmbient(IEnumerable<MappingValue> values) => new(MethodTypeParameters, values.ToImmutableArray());

    public MappingContext ForHelper() =>
        new(
            MethodTypeParameters,
            AmbientValues.Select((value, index) => new MappingValue(value.Name, value.Type, $"__ambient{index}")).ToImmutableArray()
        );
}
