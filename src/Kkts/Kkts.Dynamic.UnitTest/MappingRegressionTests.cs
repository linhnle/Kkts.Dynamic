using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class MappingRegressionTests
    {
        private static PropertyBinding Binding(string entity, string dto = null, BindingMode mode = BindingMode.TwoWay)
        {
            return new PropertyBinding { EntityProperty = entity, DtoProperty = dto, Mode = mode };
        }

        private static (DynamicAssembly Assembly, Class Class) Declare<T>(params PropertyBinding[] bindings)
        {
            var assembly = new DynamicAssembly();
            var cls = assembly.DeclareClass("Dto", typeof(T), bindings);
            return (assembly, cls);
        }

        private static object Get(object instance, string name) => instance.GetType().GetProperty(name).GetValue(instance);
        private static void Set(object instance, string name, object value) => instance.GetType().GetProperty(name).SetValue(instance, value);

        [Fact]
        public void PublicConfigurationFieldAndClassSnapshotsRemainCompatible()
        {
            var field = typeof(Class).GetField(nameof(Class.MaxRecursionDepthToBuildSelectorExpression));
            Assert.NotNull(field);
            Assert.True(field.IsStatic);
            Assert.False(field.IsInitOnly);
            Assert.Equal(typeof(uint), field.FieldType);
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass("Dto", typeof(Item), new[] { Binding("Id") });
                var snapshot = assembly.Classes;
                snapshot[0] = null;
                assembly.DeclareClass("OtherDto", typeof(Item), new[] { Binding("Name") });
                Assert.Single(snapshot);
                Assert.Equal(2, assembly.Classes.Length);
                Assert.Contains(cls, assembly.Classes);
            }
        }

        [Fact]
        public void MixedDtoParentPathsReverseMapIntoTheSameEntityChild()
        {
            var declaration = Declare<Parent>(
                Binding("Child.Id", "Nested.Id"),
                Binding("Child.Name", "Name"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Mapper.MapFromEntityToDto(dto, new Parent { Child = new Item { Id = 5, Name = "child" } });
                var entity = new Parent();
                Mapper.MapFromDtoToEntity(dto, entity);
                Assert.Equal(5, entity.Child.Id);
                Assert.Equal("child", entity.Child.Name);
            }
        }

        [Fact]
        public void MultiLevelDtoPathsShareGeneratedClassesAndMapBothWays()
        {
            var declaration = Declare<Parent>(
                Binding("Child.Id", "Nested.Details.Id"),
                Binding("Child.Name", "Nested.Details.Name"),
                Binding("Title", "Nested.Title"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Mapper.MapFromEntityToDto(dto, new Parent { Child = new Item { Id = 6, Name = "child" }, Title = "parent" });
                var nested = Get(dto, "Nested");
                Assert.Equal(6, Get(Get(nested, "Details"), "Id"));
                Assert.Equal("child", Get(Get(nested, "Details"), "Name"));
                Assert.Equal("parent", Get(nested, "Title"));
                var entity = new Parent();
                Mapper.MapFromDtoToEntity(dto, entity);
                Assert.Equal(6, entity.Child.Id);
                Assert.Equal("child", entity.Child.Name);
                Assert.Equal("parent", entity.Title);
            }
        }

        [Fact]
        public void GetterOnlyPropertyMapsToDtoAndSelector()
        {
            var declaration = Declare<ReadOnlyEntity>(Binding("Id", mode: BindingMode.OneWayToDto));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Mapper.MapFromEntityToDto(dto, new ReadOnlyEntity());
                Assert.Equal(7, Get(dto, "Id"));
                var projected = declaration.Class.BuildSelectorExpression().Compile().DynamicInvoke(new ReadOnlyEntity());
                Assert.Equal(7, Get(projected, "Id"));
            }
        }

        [Fact]
        public void SetterOnlyPropertySupportsOneWayToEntity()
        {
            var declaration = Declare<WriteOnlyEntity>(Binding("Id", mode: BindingMode.OneWayToEntity));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Set(dto, "Id", 9);
                var entity = new WriteOnlyEntity();
                Mapper.MapFromDtoToEntity(dto, entity);
                Assert.Equal(9, entity.Value);
            }
        }

        [Theory]
        [InlineData(nameof(AccessorEntity.PrivateGetter), BindingMode.OneWayToDto)]
        [InlineData(nameof(AccessorEntity.PrivateSetter), BindingMode.TwoWay)]
        [InlineData(nameof(AccessorEntity.ReadOnlyField), BindingMode.TwoWay)]
        [InlineData("Item", BindingMode.OneWayToDto)]
        public void UnsupportedAccessorsFailDuringBuild(string path, BindingMode mode)
        {
            var declaration = Declare<AccessorEntity>(Binding(path, "Value", mode));
            using (declaration.Assembly)
                Assert.Throws<MappingValidationException>(() => declaration.Assembly.Build());
        }

        [Fact]
        public void OmittedDtoNameAndNullablePrimaryKeyAreSupported()
        {
            var binding = Binding("Id");
            binding.IsPrimaryKey = true;
            var declaration = Declare<NullableEntity>(binding);
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Mapper.MapFromEntityToDto(dto, new NullableEntity { Id = 12 });
                Assert.Equal(12, Get(dto, "Id"));
                Assert.Equal(12, dto.GetIds()[0]);
                Mapper.MapFromEntityToDto(dto, new NullableEntity());
                Assert.Null(dto.GetIds()[0]);
                Assert.Null(binding.DtoProperty);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(".Id")]
        [InlineData("Child..Id")]
        [InlineData("Id.")]
        public void InvalidPathsAreRejectedAtDeclaration(string path)
        {
            using (var assembly = new DynamicAssembly())
            {
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Dto", typeof(Item), new[] { Binding(path, "Id") }));
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Dto", typeof(Item), new[] { Binding("Id", path ?? "") }));
                Assert.Empty(assembly.Classes);
            }
        }

        [Fact]
        public void InvalidAndConflictingBindingsAreRejected()
        {
            using (var assembly = new DynamicAssembly())
            {
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Dto", typeof(Item), new PropertyBinding[] { null }));
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Dto", typeof(Item), new[] { Binding("Id", mode: (BindingMode)99) }));
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Dto", typeof(Item), new[] { Binding("Id", "Id"), Binding("Id", "id") }));
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Dto", typeof(Item), new[] { Binding("Id", "First"), Binding("Id", "Second") }));
                var primaryKey = Binding("Id", "Nested.Id");
                primaryKey.IsPrimaryKey = true;
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Dto", typeof(Item), new[] { primaryKey }));
            }
            var declaration = Declare<Item>(Binding("Id", "Value"), Binding("Id", "Value.Id", BindingMode.OneWayToDto));
            using (declaration.Assembly)
                Assert.Throws<MappingValidationException>(() => declaration.Assembly.Build());
        }

        [Fact]
        public void DuplicateClassDeclarationOnlyAcceptsEquivalentMappings()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass("Dto", typeof(Item), new[] { Binding("Id") });
                Assert.Same(cls, assembly.DeclareClass("Dto", typeof(Item), new[] { Binding("Id", "Id") }));
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Dto", typeof(Item), new[] { Binding("Name") }));
            }
        }

        [Fact]
        public void FailedBuildCannotBeReusedOrExposeAnIncompleteType()
        {
            var declaration = Declare<Item>(Binding("Missing"));
            using (declaration.Assembly)
            {
                var original = Assert.Throws<MappingValidationException>(() => declaration.Assembly.Build());
                var retry = Assert.Throws<InvalidOperationException>(() => declaration.Assembly.Build());
                Assert.Same(original, retry.InnerException);
                Assert.Throws<InvalidOperationException>(() => declaration.Class.GetBuiltType());
                Assert.Throws<InvalidOperationException>(() => declaration.Class.BuildSelectorExpression());
                Assert.Throws<InvalidOperationException>(() => declaration.Assembly.DeclareClass("Other", typeof(Item), new[] { Binding("Id") }));
            }
        }

        [Fact]
        public void BuildIsIdempotentAndDisposedBuildersRejectUse()
        {
            var declaration = Declare<Item>(Binding("Id"));
            declaration.Assembly.Build();
            var type = declaration.Class.GetBuiltType();
            declaration.Assembly.Build();
            Assert.Same(type, declaration.Class.GetBuiltType());
            Assert.Throws<InvalidOperationException>(() => declaration.Assembly.DeclareClass("Other", typeof(Item), new[] { Binding("Id") }));
            ((IDisposable)declaration.Assembly).Dispose();
            ((IDisposable)declaration.Assembly).Dispose();
            Assert.Throws<ObjectDisposedException>(() => declaration.Assembly.Build());
            Assert.Throws<ObjectDisposedException>(() => declaration.Class.GetBuiltType());
            var dto = Activator.CreateInstance(type);
            Mapper.MapFromEntityToDto(dto, new Item { Id = 8 });
            Assert.Equal(8, Get(dto, "Id"));
        }

        [Fact]
        public void ParameterizedNestedConstructorFailsBeforeIlGeneration()
        {
            var declaration = Declare<ConstructorParent>(Binding("Child.Id", "ChildId"));
            using (declaration.Assembly)
            {
                var error = Assert.Throws<MappingValidationException>(() => declaration.Assembly.Build());
                Assert.Contains("parameterless constructor", error.Message);
            }
        }

        [Theory]
        [InlineData("Child")]
        [InlineData("Items")]
        public void MappedAlternativesValidateEntityConstructors(string path)
        {
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass("Parent", typeof(ConstructorCollectionParent), new[] { Binding(path) });
                assembly.DeclareClass("Child", typeof(Parameterized), new[] { Binding("Id") });
                Assert.Throws<MappingValidationException>(() => assembly.Build());
            }
        }

        [Fact]
        public void NestedAlternativeSelectorsAndFlattenedBindingsShareTheSameDtoNode()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass("Parent", typeof(Parent), new[] {
                        Binding("Child", "Nested.Child"), Binding("Child.Id", "Nested.Child.ExtraId", BindingMode.OneWayToDto)
                    });
                assembly.DeclareClass("Item", typeof(Item), new[] { Binding("Id") });
                assembly.Build();
                var selector = cls.BuildSelectorExpression().Compile();
                var result = selector.DynamicInvoke(new Parent { Child = new Item { Id = 14 } });
                var child = Get(Get(result, "Nested"), "Child");
                Assert.Equal(14, Get(child, "Id"));
                Assert.Equal(14, Get(child, "ExtraId"));
                var empty = selector.DynamicInvoke(new Parent());
                Assert.NotNull(Get(empty, "Nested"));
                Assert.Null(Get(Get(empty, "Nested"), "Child"));
            }
        }

        [Fact]
        public void SelectorsCanBeUsedWithLinqToObjectsQueryable()
        {
            var declaration = Declare<Item>(Binding("Id"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var source = new[] { new Item { Id = 23 } }.AsQueryable();
                var selector = declaration.Class.BuildSelectorExpression();
                var call = System.Linq.Expressions.Expression.Call(typeof(Queryable), nameof(Queryable.Select),
                    new[] { typeof(Item), declaration.Class.GetBuiltType() }, source.Expression,
                    System.Linq.Expressions.Expression.Quote(selector));
                var projected = source.Provider.CreateQuery(call).Cast<object>().Single();
                Assert.Equal(23, Get(projected, "Id"));
            }
        }
        [Fact]
        public void GetterOnlyMappingDoesNotRequireNestedConstructors()
        {
            var declaration = Declare<ConstructorParent>(Binding("Child.Id", "ChildId", BindingMode.OneWayToDto));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Mapper.MapFromEntityToDto(dto, new ConstructorParent { Child = new Parameterized(6) });
                Assert.Equal(6, Get(dto, "ChildId"));
            }
        }

        [Fact]
        public void ReverseMappingSelectsParameterlessConstructorNotFirstConstructor()
        {
            var declaration = Declare<OverloadedParent>(Binding("Child.Id", "ChildId"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Set(dto, "ChildId", 15);
                var entity = new OverloadedParent();
                Mapper.MapFromDtoToEntity(dto, entity);
                Assert.Equal(15, entity.Child.Id);
            }
        }

        [Fact]
        public void NullNestedSourcePreservesExistingValues()
        {
            var declaration = Declare<Parent>(Binding("Child.Id", "ChildId"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Set(dto, "ChildId", 19);
                Mapper.MapFromEntityToDto(dto, new Parent());
                Assert.Equal(19, Get(dto, "ChildId"));
                var projected = declaration.Class.BuildSelectorExpression().Compile().DynamicInvoke(new Parent());
                Assert.Equal(0, Get(projected, "ChildId"));
            }
        }

        [Fact]
        public void NullDtoSubtreePreservesExistingEntity()
        {
            var declaration = Declare<Parent>(Binding("Child.Id", "Nested.Id"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var entity = new Parent { Child = new Item { Id = 20 } };
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Mapper.MapFromDtoToEntity(dto, entity);
                Assert.Equal(20, entity.Child.Id);
            }
        }

        [Fact]
        public void NestedScalarSelectorsMatchFreshRuntimeMapping()
        {
            var declaration = Declare<Parent>(
                Binding("Child.Id", "Nested.Id"),
                Binding("Child.Name", "Nested.Name"),
                Binding("Title", "Title"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var selector = declaration.Class.BuildSelectorExpression().Compile();
                foreach (var source in new[] { new Parent(), new Parent { Child = new Item { Id = 3, Name = "child" }, Title = "title" } })
                {
                    var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                    Mapper.MapFromEntityToDto(dto, source);
                    var projected = selector.DynamicInvoke(source);
                    Assert.Equal(Get(dto, "Title"), Get(projected, "Title"));
                    if (source.Child == null)
                    {
                        Assert.Null(Get(dto, "Nested"));
                        Assert.Null(Get(projected, "Nested"));
                    }
                    else
                    {
                        Assert.Equal(Get(Get(dto, "Nested"), "Id"), Get(Get(projected, "Nested"), "Id"));
                        Assert.Equal(Get(Get(dto, "Nested"), "Name"), Get(Get(projected, "Nested"), "Name"));
                    }
                }
            }
        }

        [Fact]
        public void AlternateObjectsArraysAndCollectionsProjectAndReverseMap()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass("Parent", typeof(Parent), new[] { Binding("Child"), Binding("Items"), Binding("Array") });
                assembly.DeclareClass("Item", typeof(Item), new[] { Binding("Id"), Binding("Name") });
                assembly.Build();
                var selector = cls.BuildSelectorExpression().Compile();
                var source = new Parent
                {
                    Child = new Item { Id = 2, Name = "nested" },
                    Items = new List<Item> { new Item { Id = 3 }, null },
                    Array = new[] { null, new Item { Id = 4 } }
                };
                var dto = Activator.CreateInstance(cls.GetBuiltType());
                Mapper.MapFromEntityToDto(dto, source);
                var projected = selector.DynamicInvoke(source);
                foreach (var value in new[] { dto, projected })
                {
                    Assert.Equal(2, Get(Get(value, "Child"), "Id"));
                    Assert.Equal("nested", Get(Get(value, "Child"), "Name"));
                    var items = ((IEnumerable)Get(value, "Items")).Cast<object>().ToArray();
                    Assert.Equal(2, items.Length);
                    Assert.Equal(3, Get(items[0], "Id"));
                    Assert.Null(items[1]);
                    var array = (Array)Get(value, "Array");
                    Assert.Null(array.GetValue(0));
                    Assert.Equal(4, Get(array.GetValue(1), "Id"));
                    var target = new Parent();
                    Mapper.MapFromDtoToEntity(value, target);
                    Assert.Equal(2, target.Child.Id);
                    Assert.Equal(3, target.Items[0].Id);
                    Assert.Null(target.Items[1]);
                    Assert.Null(target.Array[0]);
                    Assert.Equal(4, target.Array[1].Id);
                }
                var empty = selector.DynamicInvoke(new Parent());
                Assert.Null(Get(empty, "Child"));
                Assert.Null(Get(empty, "Items"));
                Assert.Null(Get(empty, "Array"));
                var retained = Get(dto, "Child");
                Mapper.MapFromEntityToDto(dto, new Parent());
                Assert.Same(retained, Get(dto, "Child"));
                Assert.Null(Get(dto, "Items"));
                Assert.Null(Get(dto, "Array"));
            }
        }

        [Fact]
        public void CollectionHelpersSupportNonGenericAndSingleUseEnumerables()
        {
            var declaration = Declare<Item>(Binding("Id"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var type = declaration.Class.GetBuiltType();
                var dtos = (IList)Mapper.ToDtoCollection(type, new SingleUseEnumerable(new Item { Id = 5 }, null));
                Assert.Equal(5, Get(dtos[0], "Id"));
                Assert.Null(dtos[1]);
                var entities = (List<Item>)Mapper.ToEntityCollection(typeof(Item), new ArrayList(dtos));
                Assert.Equal(5, entities[0].Id);
                Assert.Null(entities[1]);
                Assert.Empty((List<Item>)Mapper.ToEntityCollection(typeof(Item), new ArrayList()));
                var arrays = (Item[])Mapper.ToEntityArray(typeof(Item), dtos.Cast<object>().ToArray());
                Assert.Equal(5, arrays[0].Id);
                Assert.Null(arrays[1]);
            }
        }

        [Fact]
        public void UnsupportedMapperInputsThrowInsteadOfSilentlyDoingNothing()
        {
            var declaration = Declare<Item>(Binding("Id"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Assert.Throws<ArgumentException>(() => Mapper.MapFromEntityToDto(new object(), new Item()));
                Assert.Throws<ArgumentException>(() => Mapper.MapFromDtoToEntity(new object(), new Item()));
                Assert.Throws<ArgumentException>(() => Mapper.MapFromEntityToDto(dto, new Parent()));
                Assert.Throws<ArgumentException>(() => new object().GetIds());
                Assert.Throws<ArgumentException>(() => Mapper.ToDtoArray(dto.GetType(), new object()));
                Assert.Throws<ArgumentException>(() => Mapper.ToEntityArray(typeof(Item), new object[1, 1]));
                Assert.Throws<ArgumentException>(() => Mapper.ToDtoCollection(dto.GetType(), new object()));
                Assert.Throws<ArgumentException>(() => Mapper.ToEntityCollection(typeof(Item), new object()));
                Mapper.MapFromEntityToDto(null, new Item());
                Mapper.MapFromDtoToEntity(dto, null);
                Assert.Null(Mapper.ToDtoArray(dto.GetType(), null));
                Assert.Null(Mapper.ToEntityCollection(typeof(Item), null));
                Assert.Empty(DtoObject.GetIds(null));
            }
        }

        [Fact]
        public void GetterExceptionsAreNotWrappedByReflection()
        {
            var declaration = Declare<ThrowingEntity>(Binding("Id", mode: BindingMode.OneWayToDto));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var dto = Activator.CreateInstance(declaration.Class.GetBuiltType());
                Assert.Throws<NotSupportedException>(() => Mapper.MapFromEntityToDto(dto, new ThrowingEntity()));
            }
        }

        [Fact]
        public void UnregisteredReferenceAndCollectionPropertiesAreNotOmittedFromSelectors()
        {
            var declaration = Declare<Parent>(Binding("Child"), Binding("Items"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var source = new Parent { Child = new Item { Id = 1 }, Items = new List<Item>() };
                var projected = declaration.Class.BuildSelectorExpression().Compile().DynamicInvoke(source);
                Assert.Same(source.Child, Get(projected, "Child"));
                Assert.Same(source.Items, Get(projected, "Items"));
            }
        }

        [Fact]
        public void RecursiveSelectorFailsExplicitlyRatherThanSilentlyOmittingMembers()
        {
            var declaration = Declare<RecursiveEntity>(Binding("Id"), Binding("Child"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var error = Assert.Throws<InvalidOperationException>(() => declaration.Class.BuildSelectorExpression());
                Assert.Contains("recursion", error.Message);
            }
        }

        [Fact]
        public void MappingDelegateCacheSupportsConcurrentCalls()
        {
            var declaration = Declare<Item>(Binding("Id"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var type = declaration.Class.GetBuiltType();
                Parallel.For(0, 100, i =>
                {
                    var dto = Activator.CreateInstance(type);
                    Mapper.MapFromEntityToDto(dto, new Item { Id = i });
                    var entity = new Item();
                    Mapper.MapFromDtoToEntity(dto, entity);
                    Assert.Equal(i, entity.Id);
                });
            }
        }

        [Fact]
        public void MappingAndFactoryCachesDoNotKeepCollectibleAssembliesAlive()
        {
            var reference = CreateCollectibleAssembly();
            for (var i = 0; i < 20 && reference.IsAlive; ++i)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(reference.IsAlive);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateCollectibleAssembly()
        {
            var declaration = Declare<Item>(Binding("Id"));
            using (declaration.Assembly)
            {
                declaration.Assembly.Build();
                var type = declaration.Class.GetBuiltType();
                var dtos = (IList)Mapper.ToDtoCollection(type, new[] { new Item { Id = 2 } });
                Mapper.ToEntityCollection(typeof(Item), dtos);
                dtos[0].GetIds();
                return new WeakReference(type);
            }
        }

        public class Item { public int Id { get; set; } public string Name { get; set; } }
        public class Parent
        {
            public Item Child { get; set; }
            public List<Item> Items { get; set; }
            public Item[] Array { get; set; }
            public string Title { get; set; }
        }
        public class ReadOnlyEntity { public int Id => 7; }
        public class WriteOnlyEntity { public int Value; public int Id { set => Value = value; } }
        public class NullableEntity { public int? Id { get; set; } }
        public class AccessorEntity
        {
            public int PrivateGetter { private get; set; }
            public int PrivateSetter { get; private set; }
            public readonly int ReadOnlyField;
            public int this[int index] => index;
        }
        public class ConstructorParent { public Parameterized Child { get; set; } }
        public class ConstructorCollectionParent
        {
            public Parameterized Child { get; set; }
            public List<Parameterized> Items { get; set; }
        }
        public class Parameterized { public Parameterized(int id) { Id = id; } public int Id { get; set; } }
        public class OverloadedParent { public Overloaded Child { get; set; } }
        public class Overloaded
        {
            public Overloaded(int id) { Id = id; }
            public Overloaded() { }
            public int Id { get; set; }
        }
        public class ThrowingEntity { public int Id => throw new NotSupportedException("Getter failed."); }
        public class RecursiveEntity { public int Id { get; set; } public RecursiveEntity Child { get; set; } }
        private sealed class SingleUseEnumerable : IEnumerable
        {
            private readonly object[] _items;
            private bool _enumerated;
            public SingleUseEnumerable(params object[] items) { _items = items; }
            public IEnumerator GetEnumerator()
            {
                if (_enumerated) throw new InvalidOperationException("Already enumerated.");
                _enumerated = true;
                return _items.GetEnumerator();
            }
        }
    }
}
