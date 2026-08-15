namespace DomainMapper.Engine;

internal sealed class MapperGenerationInputComparer : IEqualityComparer<MapperGenerationInput>
{
    public static MapperGenerationInputComparer Instance { get; } = new();

    public bool Equals(MapperGenerationInput? x, MapperGenerationInput? y) =>
        ReferenceEquals(x, y) || x != null && y != null && x.Fingerprint == y.Fingerprint;

    public int GetHashCode(MapperGenerationInput obj) => obj.Fingerprint.GetHashCode();
}
