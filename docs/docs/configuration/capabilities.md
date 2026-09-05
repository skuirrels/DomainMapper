# Capabilities and limitations

This page is the authoritative product contract for the next DomainMapper 1.2 release. Generated mappings use direct C# calls and member access; none of these features uses runtime reflection, assembly scanning, or mutable runtime configuration.

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

Nested paths use generated null propagation. Eligible contracts can also expose a separately declared, cached expression-tree projection.

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

`MapCollection` selects `Replace`, `ClearAndFill`, or `Append` for one allow-listed collection member. Clear/fill and append require a mutable `ICollection<T>` or `IDictionary<TKey, TValue>` contract. They preserve source ordering and duplicates; duplicate dictionary keys follow `IDictionary.Add` and throw. A null source clears under `ClearAndFill`, is a no-op under `Append`, and can instead use `PreserveTarget` or `Throw`. DomainMapper never infers identity, ownership, deletion, or merge-by-key behavior.

## Null behavior

`MapNull` supports:

- `Assign` for compatible nullable targets (the default);
- `PreserveTarget` for existing-target mappings;
- `Throw` for required runtime input;
- `EmptyCollection` for supported collection and dictionary targets.
- `MapNullSubstitute` for a compatible compile-time constant.

Nullable-to-non-nullable conversion without an explicit safe policy or conversion fails generation. Nullable roots map to nullable roots with a generated null guard. Nested paths use null-conditional member access.

Nullable value types lift through the same conversions as their underlying types: `int?` maps to a nullable value object whenever `int` maps to that value object through a `[DomainFactory]`, a single-value constructor, or a convention helper, and a nullable reference source lifts into a nullable value-type target the same way. Implicit lifted conversions stay direct. Projections keep the implicit pure subset only.

## Construction, completion, and domain boundaries

Target-owned factories selected by `MapToFactory` remain mandatory and fail closed. Mapper-owned `DomainFactory` methods support whole-source or member inputs. Additional mapping parameters bind by name to target factory parameters, constructor parameters, and settable members alike: at the mapping's own root they take precedence over a same-named source member, which then counts as unconsumed under source completeness, and in nested helpers they fill members the nested source does not provide. Root state matched only by an additional parameter must be writable, otherwise `DMPR101` is reported.

Convention construction of a target that declares an accessible static factory method reports warning `DMPR108` once per mapping and target, whether the constructor is called at the root, in a nested helper, or as a single-value wrap. `[IgnoreTargetFactory(typeof(T), Reason = "...")]` records the decision per mapping and type and is validated as stale when unused; `WarningsAsErrors` promotes the warning to an error project-wide. Generator diagnostics do not observe `.editorconfig` severities or `#pragma warning`.

`MapAfter` binds ordered, typed static completion hooks. Hooks run after generated assignments and receive supported combinations of source, target, and additional parameters. Prefer constructors, target factories, and domain methods for invariant-bearing behavior; hooks must not mutate private state or bypass an aggregate boundary.

## Enums

Enum pairs map by member name through a generated switch expression: exact names first, then a unique case-insensitive match. Every source member must have a target member, so renames, aliased source values, and `[Flags]` enums fail at build time with `DMPR109` unless a `[DomainFactory]` handles the pair. Target enums may declare additional members. Source values outside the declared members throw `InvalidOperationException` at runtime rather than mapping to a default. Nullable enums lift like other nullable value types. Projections do not include enum switches and report `DMPR106`.

## Collections and recursive graphs

Arrays, lists, enumerable/read-only collection interfaces, and mutable/read-only dictionary interfaces are generated without LINQ allocation. Countable indexed inputs use capacity-aware loops; general `IEnumerable<T>` inputs are enumerated once.

Recursive contracts are rejected by default. `[MapMaxDepth(n)]` opts a method into bounded recursion using an integer argument on generated helpers. `[MapReferenceTracking]` instead enables invocation-local reference-identity tracking for mutable targets that can be allocated before their members are assigned. Repeated references, self-cycles, and multi-object cycles reuse the same target instance. A previously tracked reference resolves before the depth policy; depth applies only when allocating a new target. Ordinary mappings allocate no tracker.

Reference tracking keys each source identity by its generated target contract, is never shared between calls or threads, and does not infer domain or persistence keys. The same source can therefore participate in more than one target shape without confusing their tracked instances. Constructor-only, required/init-only, factory-created, nullable-root, and existing-target tracking shapes fail with `DMPR105` rather than changing construction semantics.

## Closed-world runtime registry

`[MapRegistry]` on a mapper generates `TryMapRuntime(object, Type, out object?)` and `MapRuntime(object, Type)`. Only successfully generated static, non-generic, one-parameter create mappings in that mapper participate. Dispatch uses exact source types by default and direct calls; `[MapRegistryDerived]` explicitly permits assignable derived-source dispatch for one pair. Unknown pairs return `false` or make `MapRuntime` throw `InvalidOperationException` with a stable source/target message, and duplicate pairs produce `DMPR107`.

Registry methods are stateless and thread-safe. Declared collection-to-collection mapping methods can participate like any other known pair. DomainMapper does not scan assemblies, resolve services, or choose a pair from runtime business state.

## Provider-neutral projections

Install the independent `DomainMapper.Projections` contract package alongside `DomainMapper`, then bind a parameterless `Expression<Func<TSource, TTarget>>` method to an existing mapping with `[MapProjection(nameof(Map))]`. The generator emits one cached expression instance containing direct construction and member access. Consumers compose and execute it using standard query APIs.

The supported subset includes constructors, member initialization, renames, nested paths, null propagation, null substitution, and implicit pure conversions. Completion hooks, conditions, factories, reference tracking, depth guards, collection mutation, additional parameters, recursive shapes, and unsupported conversions produce `DMPR106`. DomainMapper never compiles the expression, materializes a query, inserts `AsEnumerable`, or catches provider translation failures.

## Mapping composition and inheritance

Nested values whose source and target types exactly match a declared static, non-generic, single-parameter mapping method in the same mapper are mapped by calling that method, so child entities honour their own `[MapToFactory]`, bindings, ignores, and null policies. Reuse never applies to a mapping's own root pair, inside `[MapMaxDepth]` or `[MapReferenceTracking]` contexts, or when more than one declared method matches the pair (`DMPR102`). Declared methods that reuse each other in a cycle are rejected with `DMPR102`; `[MapMaxDepth]` on one of them breaks the cycle. Projections cannot call mapping methods, so a nested pair owned by a declared method with explicit configuration or a target factory is not projectable (`DMPR106`).

Accessible inherited properties participate in convention and completeness checks. Explicitly configured inherited fields participate in the same contract. `IncludeMapping(nameof(BaseMap))` reuses the explicit member bindings from a base mapping in the same mapper. Included mappings are resolved at build time; a derived mapping may override a binding without mutating the base contract, while missing, ambiguous, cyclic, or conflicting includes produce `DMPR102`.

## Current limitations

- Projection collection transforms and arbitrary method calls are not in the provider-neutral expression subset.
- Reference tracking requires mutable two-phase target construction.
- The runtime registry has no Microsoft DI adapter; direct generated methods remain preferred when types are known.
- No unrestricted runtime derived-type dispatch or reflection scanning.
- No private-member mutation.

Provider execution behavior, persistence semantics, query filtering/paging, and consumer migration remain the consuming application's responsibility.
