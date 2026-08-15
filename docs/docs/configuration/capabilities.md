# Capabilities and limitations

This page is the authoritative product contract for DomainMapper 1.1.0. Generated mappings use direct C# calls and member access; none of these features uses runtime reflection or mutable runtime configuration.

## Explicit mapping contract

```csharp
[DomainMapper]
public static partial class OrderMapper
{
    [MapMember(nameof(OrderDto.CreatedDate), nameof(Order.DateCreated))]
    [MapMember(
        nameof(OrderDto.WarehouseDescription),
        nameof(Order.Warehouse) + "." + nameof(Warehouse.Description))]
    [IgnoreTargetMember(nameof(OrderDto.InternalState), Reason = "not part of the API contract")]
    [IgnoreSourceMember(nameof(Order.ChangeTracker))]
    [MappingCompleteness(MappingCompleteness.Both)]
    public static partial OrderDto Map(Order source, string timeZone);

    [MapTargetMember(nameof(Map), nameof(OrderDto.DisplayDate))]
    private static string DisplayDate(Order source, string timeZone) =>
        Format(source.DateCreated, timeZone);
}
```

- `MapMember` binds a target property or field to a source property/field or dot-separated nested path. Every segment is resolved semantically; missing, inaccessible, or case-insensitively ambiguous segments produce `DMPR102`.
- `MapTargetMember` binds one static typed helper to one member of one named mapping method. Helpers may consume the complete source, the matched member, or additional mapping parameters.
- Explicit forward and reverse methods are independent contracts. DomainMapper does not infer that a custom rule is reversible.

Convention mapping remains property-only for compatibility with 1.0. Fields participate when an explicit contract such as `MapMember`, `MapTargetMember`, or an existing-target allow-list names them.

Nested paths use generated null propagation. They are in-memory mappings; query projections are not yet supported.

## Completeness and ignores

The default is `MappingCompleteness.Target`: every eligible writable convention property and explicitly configured field must be mapped or explicitly ignored. `Source` requires every readable source property/field to be consumed or ignored; `Both` enforces both sides. `None` is an explicit compatibility escape hatch and reports `DMPR104`.

Ignores are method-scoped, validated against the declared source or target type, and may include a review reason. Adding a member under source completeness causes `DMPR103` at build time.

## Existing-target updates

```csharp
[MapOnlyTargetMembers(nameof(Booking.RequiredDeliveryDate), nameof(Booking.RequiredDeliveryDateBy))]
[MapNull(nameof(Booking.RequiredDeliveryDateBy), NullMemberBehavior.PreserveTarget)]
public static partial void Apply(BookingUpdate source, Booking target);

[MapCondition(nameof(Apply), nameof(Booking.RequiredDeliveryDate))]
private static bool ShouldApplyDate(BookingUpdate source, Booking target) =>
    source.HasRequiredDeliveryDate && !target.IsClosed;
```

`MapOnlyTargetMembers` is an explicit mutation allow-list. Members outside it are not assigned, which makes identity, audit, navigation, ownership, and concurrency state protected by default. A false `MapCondition` preserves the current target member. Reference targets are mutated in place; value-type targets require `ref`.

Collection relationship updates are not inferred. An update may replace an allow-listed collection member, but clear/fill, append, and merge-by-key semantics are not currently generated.

## Null behavior

`MapNull` supports:

- `Assign` for compatible nullable targets (the default);
- `PreserveTarget` for existing-target mappings;
- `Throw` for required runtime input;
- `EmptyCollection` for supported collection and dictionary targets.
- `MapNullSubstitute` for a compatible compile-time constant.

Nullable-to-non-nullable conversion without an explicit safe policy or conversion fails generation. Nullable roots map to nullable roots with a generated null guard. Nested paths use null-conditional member access.

## Construction, completion, and domain boundaries

Target-owned factories selected by `MapToFactory` remain mandatory and fail closed. Mapper-owned `DomainFactory` methods support whole-source or member inputs. Additional mapping parameters take precedence at a target factory boundary.

`MapAfter` binds ordered, typed static completion hooks. Hooks run after generated assignments and receive supported combinations of source, target, and additional parameters. Prefer constructors, target factories, and domain methods for invariant-bearing behavior; hooks must not mutate private state or bypass an aggregate boundary.

## Collections and recursive graphs

Arrays, lists, enumerable/read-only collection interfaces, and mutable/read-only dictionary interfaces are generated without LINQ allocation. Countable indexed inputs use capacity-aware loops; general `IEnumerable<T>` inputs are enumerated once.

Recursive contracts are rejected by default. `[MapMaxDepth(n)]` opts a method into bounded recursion using an integer argument on generated helpers; ordinary non-recursive mappings pay no reference-tracker allocation. Exhaustion returns `default` by default or throws when `ExhaustionBehavior = DepthExhaustionBehavior.Throw`. Reference preservation and merge-by-identity are not currently supported.

## Mapping composition and inheritance

Accessible inherited properties participate in convention and completeness checks. Explicitly configured inherited fields participate in the same contract. `IncludeMapping(nameof(BaseMap))` reuses the explicit member bindings from a base mapping in the same mapper. Included mappings are resolved at build time; a derived mapping may override a binding without mutating the base contract, while missing, ambiguous, cyclic, or conflicting includes produce `DMPR102`.

## Current limitations

- No `IQueryable`/EF Core/OData projection expressions.
- No generated runtime registry or Microsoft DI facade.
- No reference-preserving cycle tracker.
- No unrestricted runtime derived-type dispatch or reflection scanning.
- No private-member mutation.

These limitations are replacement blockers for affected eDC paths; those paths must remain on their current implementation or use an explicit application-owned rewrite until the corresponding feature is delivered.
