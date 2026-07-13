using System.Reflection;
using DomainMap.Diagnostics;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DomainMap.Configuration;

internal static class MapperBuildConfigurationReader
{
    private const string DomainMapBuildPropertyNamePrefix = "DomainMap";
    private const string BuildPropertyPrefix = "build_property.";

    private static readonly IReadOnlyCollection<PropertyInfo> _properties = typeof(MapperConfiguration).GetProperties(
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
    );

    public static (MapperConfiguration Configuration, ImmutableEquatableArray<Diagnostic> Diagnostics) Read(AnalyzerConfigOptions options)
    {
        var configuration = new MapperConfiguration();
        var diagnostics = new List<Diagnostic>();

        foreach (var property in _properties)
        {
            var configName = DomainMapBuildPropertyNamePrefix + property.Name;
            if (!options.TryGetValue(BuildPropertyPrefix + configName, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType.IsEnum)
            {
                if (!TryParseEnum(propertyType, value, out var enumValue))
                {
                    diagnostics.Add(
                        Diagnostic.Create(
                            DiagnosticDescriptors.ConfiguredMSBuildOptionInvalid,
                            null,
                            configName,
                            value,
                            propertyType.FullName
                        )
                    );
                    continue;
                }

                property.SetValue(configuration, enumValue);
            }
            else if (propertyType == typeof(bool))
            {
                if (!bool.TryParse(value, out var boolValue))
                {
                    diagnostics.Add(
                        Diagnostic.Create(
                            DiagnosticDescriptors.ConfiguredMSBuildOptionInvalid,
                            null,
                            configName,
                            value,
                            propertyType.FullName
                        )
                    );
                    continue;
                }

                property.SetValue(configuration, boolValue);
            }
            else
            {
                diagnostics.Add(
                    Diagnostic.Create(DiagnosticDescriptors.ConfiguredMSBuildOptionInvalid, null, configName, value, propertyType.FullName)
                );
            }
        }

        return (configuration, diagnostics.ToImmutableEquatableArray());
    }

    private static bool TryParseEnum(Type enumType, string value, out object? result)
    {
        try
        {
            // The Enum.Parse method only supports commas as a separator for flags.
            // MSBuild (and humans) may use other separators.
            var normalizedValue = value.Replace(';', ',').Replace('|', ',');
            result = Enum.Parse(enumType, normalizedValue, true);
            return true;
        }
        catch (ArgumentException)
        {
            result = null;
            return false;
        }
    }
}
