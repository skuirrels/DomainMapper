using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.Enumerables;

public record DictionaryInfo(CollectionInfo Collection, ITypeSymbol Key, ITypeSymbol Value);
