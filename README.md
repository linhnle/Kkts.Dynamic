# Kkts.Dynamic

[![NuGet](https://img.shields.io/nuget/v/Kkts.Dynamic.svg)](https://www.nuget.org/packages/Kkts.Dynamic)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/linhnle/Kkts.Dynamic/blob/master/LICENSE)

**Generate DTO and view-model types at runtime, then map them to and from your entities.**

Kkts.Dynamic is a lightweight object mapper for .NET Standard 2.0. Instead of writing a DTO class for every shape, define property bindings and let the library generate the types and mapping methods.

## Features

- **Runtime DTO generation:** create concrete types implementing `IDtoObject`, without handwritten DTO classes.
- **Directional mapping:** choose two-way, entity-to-DTO, or DTO-to-entity bindings.
- **Flexible property paths:** rename properties, flatten nested entity properties, or group properties into nested DTO objects.
- **Objects and collections:** map registered entity types, arrays, and supported generic collections.
- **Primary keys:** extract single or composite identifiers in a configured order.
- **Projection expressions:** build entity-to-DTO expressions for compiled selectors and LINQ.
- **Generated mapping methods:** emitted IL and cached delegates handle property assignment and mapping dispatch.

The library requires runtime code generation (`Reflection.Emit` and compiled expressions). **Native AOT is not supported.**

## Installation

```sh
dotnet add package Kkts.Dynamic
```

Or use the NuGet Package Manager Console:

```powershell
Install-Package Kkts.Dynamic
```

Import the `Kkts.Dynamic` namespace. The examples below also use `System` and, for collections, `System.Collections.Generic`.

## Quick start

Define your entity as a public class:

```csharp
public class Product
{
    public int Id { get; set; }
    public string ProductName { get; set; }
    public double Price { get; set; }
}
```

Declare bindings, build the DTO type, and map an entity into it:

```csharp
using System;
using Kkts.Dynamic;

var assembly = new DynamicAssembly();
var productDtoClass = assembly.DeclareClass(
    "ProductDto",
    typeof(Product),
    new[]
    {
        new PropertyBinding
        {
            EntityProperty = "Id",
            DtoProperty = "Id",
            Mode = BindingMode.OneWayToDto,
            IsPrimaryKey = true,
            PrimaryKeyOrder = 0
        },
        new PropertyBinding
        {
            EntityProperty = "ProductName",
            DtoProperty = "Name",
            Mode = BindingMode.TwoWay
        },
        new PropertyBinding
        {
            EntityProperty = "Price",
            DtoProperty = "Price",
            Mode = BindingMode.TwoWay
        }
    });

assembly.Build();

var dtoType = productDtoClass.GetBuiltType();
var product = new Product { Id = 1, ProductName = "Phone", Price = 20.5 };
var dto = productDtoClass.CreateDto(product);

Console.WriteLine(dtoType.GetProperty("Name").GetValue(dto)); // Phone
Console.WriteLine(DtoObject.GetIds(dto)[0]); // 1

dtoType.GetProperty("Name").SetValue(dto, "Updated phone");
Mapper.MapFromDtoToEntity(dto, product);

Console.WriteLine(product.ProductName); // Updated phone
Console.WriteLine(product.Id); // 1: the Id binding does not write back
```

Declare all classes before calling `Build()`. Reuse built types for subsequent mappings rather than generating a new assembly for every object.

`Class.CreateDto(entity)` creates a fresh object of the selected generated type and maps its forward bindings. It requires a built, usable declaration and a non-null compatible entity; it never builds implicitly. Existing DTO-first mapper calls remain available, including explicit activation followed by `Mapper.MapFromEntityToDto(dto, entity)`, and retain their null-argument no-op behavior.

### Typed fluent bindings

Use expressions for compiler-checked entity paths, while retaining strings for runtime-defined DTO paths:

```csharp
var productDtoClass = assembly.DeclareClass<Product>("ProductDto", bindings => bindings
    .Map(p => p.Id, mode: BindingMode.OneWayToDto, isPrimaryKey: true)
    .Map(p => p.ProductName, "Name")
    .Map(p => p.Price));

assembly.Build();
var dto = productDtoClass.CreateDto(product);
```

This is an alternative declaration to the quick start, not an additional declaration after `Build()`. Omitting the DTO path uses the complete entity path. Specify `primaryKeyOrder` for composite identifiers. `.Map(new PropertyBinding { ... })` can include configuration-driven bindings in the same fluent declaration.

Member expressions support instance property/field chains rooted at the entity parameter, including nested paths and terminal boxing without changing the member type. Calculations, calls, indexers, static/captured members, and meaning-changing casts are rejected. Direction-specific accessor rules and the nested value-type restriction are the same as string bindings. Computed values belong in a separate computed-value binding, not `.Map`. Declarations snapshot configuration; later builder or binding mutations cannot change them.

## Property bindings

| Property | Purpose |
| --- | --- |
| `EntityProperty` | Entity property path, such as `ProductName` or `Category.Name`. |
| `DtoProperty` | Generated DTO property path. Defaults to `EntityProperty` when omitted. |
| `Mode` | Controls the mapping direction. Specify it explicitly for clarity. |
| `IsPrimaryKey` | Includes the property in `DtoObject.GetIds(dto)`. |
| `PrimaryKeyOrder` | Controls the order of identifiers returned for composite keys. |

| Binding mode | Entity to DTO | DTO to entity |
| --- | --- | --- |
| `TwoWay` | Yes | Yes |
| `OneWayToDto` | Yes | No |
| `OneWayToEntity` | No | Yes |

All generated DTOs implement the marker interface `IDtoObject`. `DtoObject.GetIds(dto)` returns an `object[]`; primary keys must be top-level properties on both sides.

### Nested paths, objects, and collections

For a richer entity model:

```csharp
public class CatalogProduct
{
    public string ExternalCode { get; set; }
    public string ExternalInformation { get; set; }
    public Category Category { get; set; }
    public List<Tag> Tags { get; set; }
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```

Use dot-separated paths to group or flatten properties, and register related types when their DTO shapes differ:

```csharp
var assembly = new DynamicAssembly();

var catalogDtoClass = assembly.DeclareClass("CatalogProductDto", typeof(CatalogProduct), new[]
{
    new PropertyBinding { EntityProperty = "ExternalCode", DtoProperty = "External.Code", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "ExternalInformation", DtoProperty = "External.Information", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "Category", DtoProperty = "Category", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "Category.Name", DtoProperty = "CategoryName", Mode = BindingMode.OneWayToDto },
    new PropertyBinding { EntityProperty = "Tags", DtoProperty = "Tags", Mode = BindingMode.TwoWay }
});

assembly.DeclareClass("CategoryDto", typeof(Category), new[]
{
    new PropertyBinding { EntityProperty = "Id", Mode = BindingMode.TwoWay, IsPrimaryKey = true },
    new PropertyBinding { EntityProperty = "Name", DtoProperty = "CategoryName", Mode = BindingMode.TwoWay }
});

assembly.DeclareClass("TagDto", typeof(Tag), new[]
{
    new PropertyBinding { EntityProperty = "Id", Mode = BindingMode.TwoWay, IsPrimaryKey = true },
    new PropertyBinding { EntityProperty = "Name", Mode = BindingMode.TwoWay }
});

assembly.Build();

var dto = Activator.CreateInstance(catalogDtoClass.GetBuiltType());
var product = new CatalogProduct
{
    ExternalCode = "P01",
    ExternalInformation = "Product information",
    Category = new Category { Id = 1220, Name = "Mobile" },
    Tags = new List<Tag> { new Tag { Id = 1, Name = "phone" } }
};

Mapper.MapFromEntityToDto(dto, product);
```

The generated DTO has `External.Code`, `External.Information`, a flattened `CategoryName`, a mapped `Category` object with `Id` and `CategoryName`, and a collection of mapped tags.

If a property type is not registered, its value is assigned directly rather than deep-copied. You do not need to register related types when you want to retain the entity objects unchanged.

### Mapping collections directly

Use the generated DTO type with the collection helpers:

```csharp
var products = new[] { product };
var dtoArray = Mapper.ToDtoArray(dtoType, products);
var dtoCollection = Mapper.ToDtoCollection(dtoType, products);

// Reverse helpers take the destination entity element type.
var entityArray = Mapper.ToEntityArray(typeof(Product), dtoArray);
var entityCollection = Mapper.ToEntityCollection(typeof(Product), dtoCollection);
```

Here, `product` and `dtoType` come from the quick start. Array helpers require single-dimensional, zero-based arrays; collection helpers accept enumerable inputs. Helpers return `object` because the DTO element type is generated at runtime.

## Binding and runtime requirements

- The library retains `netstandard2.0` compatibility. It requires runtime code generation (`Reflection.Emit` and compiled expressions); Native AOT is not supported.
- Entity mapping types must be visible, closed reference classes. Property paths must contain non-empty segments. Omitting `DtoProperty` uses `EntityProperty`.
- DTO paths are case-insensitive and must be unique. Multiple bindings cannot write the same entity path. Redeclaring a class name is only allowed with equivalent mappings.
- Entity-to-DTO bindings need public getters. DTO-to-entity bindings need public setters/writable fields. Indexed properties and nested value-type paths are unsupported.
- Reference-type entity getters and setters honor runtime overrides, including base-declared nested properties and compatible derived entities. Reverse mapping reuses existing derived child instances and invokes parent setter overrides when creating missing children.
- Reverse mapping creates nested/alternative entities and collection elements using public parameterless constructors. Abstract types or types without these constructors fail during `Build()`, not during mapping.
- Mapped collections support `List<T>`, `IList<T>`, `ICollection<T>`, and `IEnumerable<T>` properties; mapped arrays must be single-dimensional, zero-based arrays. Unregistered property types are assigned directly, not deep-copied.
- Mapping helpers accept enumerable implementations without relying on their concrete generic arguments and enumerate inputs once. Non-null invalid DTOs, incompatible entities, and invalid helper inputs throw descriptive exceptions instead of silently returning. Getter/setter exceptions propagate directly rather than being wrapped in `TargetInvocationException`.
- Primary keys must be top-level properties on both sides; their output order follows `PrimaryKeyOrder`.
- Configure declarations and build on one thread. A successful build is idempotent. A failed build permanently faults that assembly; create a new one. Disposal rejects further builder use, but already-created DTO types remain usable.
- Concurrent mapping of distinct objects is supported after build. Do not concurrently mutate the same destination or build/cache selector expressions on the same `Class`.
- Source-member attributes are copied for bindings that can write back to the entity and retain the source member's type. CLR-supported constructor and named property/field arguments retain their values and types, including enums, `Type`, boxed values, and typed arrays (including null and empty arrays). Forward-only, changed-type converted, computed, and type-substituted properties do not gain copied source attributes.
- Null DTO/entity arguments are no-ops. Null intermediate objects skip their bindings, preserving existing destination values. Null directly mapped scalar/collection properties are assigned; null mapped alternative objects preserve existing objects. Arrays and mapped collections preserve null elements.
- Property assignments use emitted IL. Mapping dispatch and object factories use cached delegates with weak type keys so collectible assemblies can unload. Build and first-call costs still apply; measure performance for your workload.

## Named DTO shapes and nested overlays

Declare several names for an entity, then select nested object/collection shapes explicitly:

```csharp
assembly.DeclareClass<Category>("CategorySummary", b => b.Map(c => c.Id));
assembly.DeclareClass<Category>("CategoryDetail", b => b.Map(c => c.Id).Map(c => c.Name));
assembly.DeclareClass<CatalogProduct>("CatalogSummary", b => b
    .Map(p => p.Category, nestedShape: "CategorySummary")
    .Map(p => p.ExternalCode));
```

String bindings use `NestedShape` for the same selection. A selected shape must belong to this assembly and map the exact entity or collection-element type; declarations can reference a shape declared later. Zero matching shapes retain direct assignment, and one retains automatic substitution. **Migration:** multiple matching nested shapes now fail validation instead of choosing the first registration. Set `NestedShape` explicitly for ambiguous bindings.

Additional paths beneath a mapped object (for example, `Child.Id` to `Child.ExtraId`) extend an isolated per-use DTO layout. They do not add properties to the registered base DTO or another parent's nested shape. Overlay source paths remain relative to the parent entity and can refer to other parent members. Incompatible base/overlay leaves and duplicate reverse writes are configuration errors. Without overlays, the nested DTO keeps the selected base type.

## Mapping validation

Inspect all independently detectable configuration errors before building:

```csharp
var report = assembly.Validate();
foreach (var diagnostic in report.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.ClassName} [{diagnostic.DtoPath}] " +
        $"{diagnostic.Direction}: {diagnostic.Message} {diagnostic.SuggestedFix}");
```

`ValidateClass(name, entityType, bindings)` checks a raw candidate alongside registered declarations without registering it. This is useful for configuration-driven input whose syntax would otherwise fail immediately at declaration. Reports expose `IsValid` and immutable diagnostics with severity, original zero-based binding index, entity/DTO paths, direction or configuration scope, stable code, explanation, and corrective guidance.

Validation does not build types, mutate caller configuration, or fault the assembly. Existing declaration-time argument failures remain fail-fast. `Build()` runs the same checks and throws `MappingValidationException` (an `InvalidOperationException`) with a `Report` if invalid; that failed build still permanently faults the assembly. Create a new assembly before retrying corrected declarations.

## Conversions and computed values

Declare converted member types and forward-only computations explicitly:

```csharp
var display = assembly.DeclareClass<Product>("ProductDisplay", b => b
    .MapConverted<int, string>(p => p.Id, "Id",
        value => value.ToString(), value => int.Parse(value),
        BindingMode.TwoWay, isPrimaryKey: true)
    .Compute("Label", p => p.ProductName + " (" + p.Price + ")"));
```

`MapConverted` defaults to forward-only; two-way type conversions require explicit forward and reverse expressions with exactly matching input/output types. `Compute` infers the DTO value type and is forward-only, cannot be a primary key, and owns its null navigation. Converted top-level keys return the DTO-side value from `GetIds`.

`MapConvertedInMemory` and `ComputeInMemory` accept delegates for deliberate in-memory transformations. String configuration uses `DtoValueType`, `ToDtoExpression` / `ToEntityExpression`, `ToDtoInMemory` / `ToEntityInMemory`, and `ComputedExpression` / `ComputedInMemory`. Choose one representation per direction. A forward expression can be combined with a reverse-only delegate.

Expression bodies are composed into selectors without compiled delegate invocation. Active delegate-only forward bindings, including in nested shapes, produce validation warnings and cause selector construction to fail explicitly with the shape and DTO path. They do not prevent in-memory build/mapping. Reverse-only delegates do not block forward selectors. Database translation remains the query provider's responsibility; no client-evaluation fallback is introduced.

Member conversions retain intermediate-null guards and receive directly mapped nulls when their input type allows them. Computations do not receive automatic null-navigation rewrites. Converter/getter/setter exceptions propagate directly. Same-type member bindings retain existing copied attributes; changed-type converted and computed DTO properties do not inherit potentially incompatible entity attributes. Transformation bindings do not also perform registered nested-shape substitution.

## Explicit partial updates

Use caller-supplied DTO paths to distinguish omitted fields from supplied nulls:

```csharp
var edit = assembly.DeclareClass<Product>("ProductEdit", b => b
    .Map(p => p.ProductName, "Name")
    .Map(p => p.Price));
assembly.Build();
var dto = edit.CreateDto(product);

dto.GetType().GetProperty("Name").SetValue(dto, "Updated");
Mapper.MapPartialFromDtoToEntity(dto, product, new[] { "Name" });
// Name is updated; Price is omitted and preserved.

dto.GetType().GetProperty("Name").SetValue(dto, null);
Mapper.MapPartialFromDtoToEntity(dto, product, new[] { "Name" });
// The supplied null clears ProductName.
```

This is a separate declaration example, not an addition after an earlier build. Capture presence from the request/configuration layer; a DTO's default values alone cannot indicate whether a field was supplied. The path sequence is enumerated once, matched case-insensitively, and deduplicated. An empty sequence does nothing; null arguments and incompatible DTO/entity types fail explicitly.

Select editable leaves such as `Fields.Name` or whole mapped objects/collections such as `Category` or `Tags`. Synthetic grouping objects cannot be selected as a whole. Unknown, forward-only/computed, indexed/element, and overlapping whole-object/descendant paths are rejected before reads or writes. Explicitly selected descendants beneath a null DTO parent fail rather than silently clearing or skipping.

Omitted fields do not execute getters/converters or construct entity parents. Selected scalar descendants create only required entity parents and preserve siblings. Supplied nulls clear reference/nullable targets, including mapped alternative objects; explicit reverse conversions can instead turn null into a valid value. Non-null whole mapped objects use their selected shape's full reverse mapping and applicable parent overlays once, retaining its null-navigation guards. Overlay leaves are editable only in the declaring parent's effective layout, and forward-only overlays remain excluded. Arrays/collections are replaced wholesale, cleared by null, or preserved when omitted; null elements remain intact. Element-level updates, merge-by-key, and delete-by-key are not supported.

All presence checks and selected value/conversion preparation finish before assignments. Arbitrary user getter, converter, constructor, or setter exceptions propagate directly, without a transactional rollback guarantee (including full mapping of selected objects). Existing `MapFromDtoToEntity` behavior is unchanged: notably, null mapped alternative objects still preserve existing entities. Built partial-update plans remain usable after builder disposal and use weak type ownership, not caches of arbitrary presence combinations.

## Selector expressions

`Class.BuildSelectorExpression()` creates an entity-to-DTO projection:

```csharp
var selector = productDtoClass.BuildSelectorExpression();
var compiledSelector = selector.Compile();
var projectedDto = compiledSelector.DynamicInvoke(product);
```

It supports scalars, flattened/nested objects, mapped arrays, and mapped collections, including null elements. Unregistered reference/collection properties retain direct-assignment semantics.

Null source paths yield default destination values, matching mapping into a fresh DTO rather than updating an existing one. Mapped collections are materialized. Recursive alternative bindings exceeding `Class.MaxRecursionDepthToBuildSelectorExpression` (default: 4) throw explicitly instead of silently omitting properties. Set this limit before constructing selectors; cyclic object graphs are not supported.

Compiled selectors and LINQ-to-Objects `IQueryable` projections are tested. Database query providers may not translate every expression, especially nested collection materialization; validate translation with your provider.

## Tests and performance measurements

Build with compiler warnings treated as errors:

```sh
dotnet build src/Kkts/Kkts.sln -warnaserror
```

SonarQube analysis retains narrowly scoped, justified exceptions for the existing mutable `Class.MaxRecursionDepthToBuildSelectorExpression` field and the snapshot-returning `DynamicAssembly.Classes` property to preserve public API compatibility.

Run the original and regression tests on .NET 8 and .NET 10:

```sh
dotnet test src/Kkts/Kkts.Dynamic.UnitTest/Kkts.Dynamic.UnitTest.csproj
```

Run the dependency-free performance smoke benchmark in Release mode:

```sh
dotnet run -c Release --project benchmarks/Kkts.Dynamic.Benchmarks
```

It reports build time, first-call time, median steady-state reflection versus cached-delegate dispatch, and 1,000-item collection time/allocation. Results are environment-dependent; this is a smoke benchmark, not a statistically rigorous replacement for production profiling.

## Support and license

- Package: [Kkts.Dynamic on NuGet](https://www.nuget.org/packages/Kkts.Dynamic)
- Bugs and feature requests: [GitHub issues](https://github.com/linhnle/Kkts.Dynamic/issues)
- Author: [Linh Le on LinkedIn](https://www.linkedin.com/in/linh-le-258417105/)
- License: [MIT](https://github.com/linhnle/Kkts.Dynamic/blob/master/LICENSE)
