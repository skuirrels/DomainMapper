using DomainMap.Abstractions;
using DomainMap.Diagnostics;
using DomainMap.Helpers;
using Microsoft.CodeAnalysis;

namespace DomainMap.Descriptors.ObjectFactories;

public static class ObjectFactoryBuilder
{
    public static ObjectFactoryCollection ExtractObjectFactories(SimpleMappingBuilderContext ctx, ITypeSymbol mapperSymbol, bool isStatic)
    {
        var objectFactories = ctx
            .SymbolAccessor.GetAllDirectlyAccessibleMethods(mapperSymbol)
            .Where(m => IsFactory(ctx, m))
            .Select(x => BuildObjectFactory(ctx, x, isStatic))
            .WhereNotNull()
            .ToList();

        return new ObjectFactoryCollection(objectFactories);
    }

    private static ObjectFactory? BuildObjectFactory(SimpleMappingBuilderContext ctx, IMethodSymbol methodSymbol, bool isStatic)
    {
        var mapToParameters =
            ctx.SymbolAccessor.HasAttribute<DomainFactoryAttribute>(methodSymbol)
            || ctx.AttributeAccessor.AccessFirstOrDefault<ObjectFactoryAttribute>(methodSymbol)?.MapToParameters == true;
        if (
            methodSymbol.IsAsync
            || (!mapToParameters && methodSymbol.Parameters.Length > 1)
            || methodSymbol.IsPartialDefinition
            || methodSymbol.MethodKind != MethodKind.Ordinary
            || methodSymbol.ReturnsVoid
            || (!methodSymbol.IsStatic && isStatic)
        )
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
            return null;
        }

        if (mapToParameters)
        {
            return methodSymbol.IsGenericMethod
                ? BuildGenericParameterObjectFactory(ctx, methodSymbol)
                : new ParameterObjectFactory(ctx.SymbolAccessor, methodSymbol);
        }

        if (!methodSymbol.IsGenericMethod)
        {
            return methodSymbol.Parameters.Length == 1
                ? new SimpleObjectFactoryWithSource(ctx.SymbolAccessor, methodSymbol)
                : new SimpleObjectFactory(ctx.SymbolAccessor, methodSymbol);
        }

        switch (methodSymbol.TypeParameters.Length)
        {
            case 2:
                return BuildGenericSourceTargetObjectFactory(ctx, methodSymbol);

            case 1:
                return BuildGenericSingleTypeParameterObjectFactory(ctx, methodSymbol);

            default:
                ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
                return null;
        }
    }

    internal static bool IsFactory(SimpleMappingBuilderContext ctx, IMethodSymbol methodSymbol) =>
        ctx.SymbolAccessor.HasAttribute<DomainFactoryAttribute>(methodSymbol)
        || ctx.SymbolAccessor.HasAttribute<ObjectFactoryAttribute>(methodSymbol);

    private static ObjectFactory? BuildGenericSingleTypeParameterObjectFactory(SimpleMappingBuilderContext ctx, IMethodSymbol methodSymbol)
    {
        var sourceParameter = methodSymbol.Parameters.FirstOrDefault();
        var typeParameter = methodSymbol.TypeParameters[0];
        var returnTypeIsGeneric =
            methodSymbol.ReturnType.TypeKind == TypeKind.TypeParameter
            && string.Equals(methodSymbol.ReturnType.Name, typeParameter.Name, StringComparison.Ordinal);
        var hasSourceParameter = sourceParameter != null;
        var sourceParameterIsGeneric =
            sourceParameter?.Type.TypeKind == TypeKind.TypeParameter
            && string.Equals(sourceParameter.Type.Name, typeParameter.Name, StringComparison.Ordinal);

        if (returnTypeIsGeneric && hasSourceParameter && sourceParameterIsGeneric)
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
            return null;
        }

        if (returnTypeIsGeneric)
        {
            return hasSourceParameter
                ? new GenericTargetObjectFactoryWithSource(ctx.GenericTypeChecker, ctx.SymbolAccessor, methodSymbol)
                : new GenericTargetObjectFactory(ctx.GenericTypeChecker, ctx.SymbolAccessor, methodSymbol);
        }

        if (hasSourceParameter)
            return new GenericSourceObjectFactory(ctx.GenericTypeChecker, ctx.SymbolAccessor, methodSymbol);

        ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
        return null;
    }

    private static ObjectFactory? BuildGenericSourceTargetObjectFactory(SimpleMappingBuilderContext ctx, IMethodSymbol methodSymbol)
    {
        if (methodSymbol.Parameters.Length != 1)
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
            return null;
        }

        var typeParameterNames = methodSymbol.TypeParameters.Select(tp => tp.Name).ToList();
        var sourceParameterIndex = typeParameterNames.IndexOf(methodSymbol.Parameters[0].Type.Name);
        if (sourceParameterIndex == -1)
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
            return null;
        }

        if (!typeParameterNames.Contains(methodSymbol.ReturnType.Name))
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
            return null;
        }

        return new GenericSourceTargetObjectFactory(ctx.GenericTypeChecker, ctx.SymbolAccessor, methodSymbol, sourceParameterIndex);
    }

    private static ObjectFactory? BuildGenericParameterObjectFactory(SimpleMappingBuilderContext ctx, IMethodSymbol methodSymbol)
    {
        switch (methodSymbol.TypeParameters.Length)
        {
            case 1:
                return BuildGenericTargetParameterObjectFactory(ctx, methodSymbol);

            case 2:
                return BuildGenericSourceTargetParameterObjectFactory(ctx, methodSymbol);

            default:
                ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
                return null;
        }
    }

    private static ObjectFactory? BuildGenericTargetParameterObjectFactory(SimpleMappingBuilderContext ctx, IMethodSymbol methodSymbol)
    {
        var typeParameter = methodSymbol.TypeParameters[0];
        var returnTypeIsGeneric =
            methodSymbol.ReturnType.TypeKind == TypeKind.TypeParameter
            && string.Equals(methodSymbol.ReturnType.Name, typeParameter.Name, StringComparison.Ordinal);

        if (!returnTypeIsGeneric)
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
            return null;
        }

        return new GenericTargetParameterObjectFactory(ctx.GenericTypeChecker, ctx.SymbolAccessor, methodSymbol);
    }

    private static ObjectFactory? BuildGenericSourceTargetParameterObjectFactory(
        SimpleMappingBuilderContext ctx,
        IMethodSymbol methodSymbol
    )
    {
        var typeParameterNames = methodSymbol.TypeParameters.Select(tp => tp.Name).ToList();
        var targetTypeParameterIndex = typeParameterNames.IndexOf(methodSymbol.ReturnType.Name);
        if (targetTypeParameterIndex == -1)
        {
            ctx.ReportDiagnostic(DiagnosticDescriptors.InvalidObjectFactorySignature, methodSymbol, methodSymbol.Name);
            return null;
        }

        var sourceTypeParameterIndex = (targetTypeParameterIndex + 1) % 2;
        return new GenericSourceTargetParameterObjectFactory(
            ctx.GenericTypeChecker,
            ctx.SymbolAccessor,
            methodSymbol,
            sourceTypeParameterIndex
        );
    }
}
