using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class PartialUpdateTests
    {
        public class Item
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public Item Child { get; set; }
        }
        public class Entity
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public int Id { get; set; }
            public int Field;
            public Item Child { get; set; }
            public List<Item> Items { get; set; }
            public Item[] Array { get; set; }
            public int? Optional { get; set; }
        }
        private static object Get(object instance, string name) => instance.GetType().GetProperty(name).GetValue(instance);
        private static void Set(object instance, string name, object value) => instance.GetType().GetProperty(name).SetValue(instance, value);

        [Fact]
        public void OmittedNullAndSuppliedValuesAreDistinct()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name).Map(e => e.Description).Map(e => e.Field));
                assembly.Build();
                var dto = cls.CreateDto(new Entity { Name = null, Description = "dto", Field = 4 });
                var entity = new Entity { Name = "existing", Description = "keep", Field = 9 };
                Mapper.MapPartialFromDtoToEntity(dto, entity, Array.Empty<string>());
                Assert.Equal("existing", entity.Name);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "name", "NAME", "Field" });
                Assert.Null(entity.Name);
                Assert.Equal("keep", entity.Description);
                Assert.Equal(4, entity.Field);
                Set(dto, "Name", "new");
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Name" });
                Assert.Equal("new", entity.Name);
            }
        }

        [Theory]
        [InlineData("Missing")]
        [InlineData("ReadOnly")]
        [InlineData("Computed")]
        [InlineData("Fields")]
        [InlineData("Items[0].Id")]
        [InlineData("Items.Id")]
        [InlineData("")]
        public void InvalidPresenceIsRejectedBeforeAnyMutation(string path)
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name)
                    .Map(e => e.Id, "ReadOnly", BindingMode.OneWayToDto)
                    .Map(e => e.Field, "Fields.Field")
                    .Map(e => e.Items)
                    .Compute("Computed", e => e.Description));
                assembly.DeclareClass<Item>("Item", b => b.Map(e => e.Id));
                assembly.Build();
                var dto = cls.CreateDto(new Entity { Name = "update" });
                var entity = new Entity { Name = "keep" };
                Assert.Throws<ArgumentException>(() => Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Name", path }));
                Assert.Equal("keep", entity.Name);
            }
        }

        [Fact]
        public void NullArgumentsAndIncompatibleTypesFailExplicitly()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name));
                assembly.Build();
                var dto = cls.CreateDto(new Entity());
                Assert.Throws<ArgumentNullException>(() => Mapper.MapPartialFromDtoToEntity(null, new Entity(), Array.Empty<string>()));
                Assert.Throws<ArgumentNullException>(() => Mapper.MapPartialFromDtoToEntity(dto, null, Array.Empty<string>()));
                Assert.Throws<ArgumentNullException>(() => Mapper.MapPartialFromDtoToEntity(dto, new Entity(), null));
                Assert.Throws<ArgumentException>(() => Mapper.MapPartialFromDtoToEntity(new object(), new Entity(), Array.Empty<string>()));
                Assert.Throws<ArgumentException>(() => Mapper.MapPartialFromDtoToEntity(dto, new object(), Array.Empty<string>()));
            }
        }

        [Fact]
        public void WholeObjectNullClearsOnlyInPartialMappingAndOverlapIsRejected()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Child).Map(e => e.Name));
                assembly.DeclareClass<Item>("Item", b => b.Map(e => e.Id).Map(e => e.Name));
                assembly.Build();
                var dto = cls.CreateDto(new Entity());
                var entity = new Entity { Child = new Item { Id = 3 }, Name = "keep" };
                Mapper.MapFromDtoToEntity(dto, entity);
                Assert.NotNull(entity.Child);
                entity.Name = "keep";
                Assert.Throws<ArgumentException>(() => Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Name", "Child", "Child.Id" }));
                Assert.Equal("keep", entity.Name);
                Assert.NotNull(entity.Child);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child" });
                Assert.Null(entity.Child);
            }
        }

        [Fact]
        public void NullGroupingParentDoesNotClearAnInaccessibleDescendant()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name).Map(e => e.Child.Id, "Fields.Id"));
                assembly.Build();
                var dto = cls.CreateDto(new Entity { Name = "update" });
                var entity = new Entity { Name = "keep", Child = new Item { Id = 9 } };
                Assert.Throws<ArgumentException>(() => Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Name", "Fields.Id" }));
                Assert.Equal("keep", entity.Name);
                Assert.Equal(9, entity.Child.Id);
            }
        }

        [Fact]
        public void NestedAndFlattenedWritesCreateOnlyRequiredParents()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name)
                    .Map(e => e.Child.Id, "Fields.Id")
                    .Map(e => e.Child.Name, "Labels.Name"));
                assembly.Build();
                var dto = cls.CreateDto(new Entity { Child = new Item { Id = 5, Name = "child" } });
                var entity = new Entity { Name = "preserve" };
                Mapper.MapPartialFromDtoToEntity(dto, entity, Array.Empty<string>());
                Assert.Null(entity.Child);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Fields.Id" });
                Assert.Equal(5, entity.Child.Id);
                Assert.Null(entity.Child.Name);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Labels.Name" });
                Assert.Equal("child", entity.Child.Name);
                Assert.Equal("preserve", entity.Name);
            }
        }

        [Fact]
        public void WholeCollectionsReplaceClearOrPreserveWithNullElements()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Items).Map(e => e.Array));
                assembly.DeclareClass<Item>("Item", b => b.Map(e => e.Id));
                assembly.Build();
                var dto = cls.CreateDto(new Entity { Items = new List<Item> { new Item { Id = 5 }, null }, Array = new[] { null, new Item { Id = 6 } } });
                var existing = new List<Item> { new Item { Id = 9 } };
                var entity = new Entity { Items = existing };
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Array" });
                Assert.Same(existing, entity.Items);
                Assert.Null(entity.Array[0]);
                Assert.Equal(6, entity.Array[1].Id);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Items" });
                Assert.NotSame(existing, entity.Items);
                Assert.Equal(5, entity.Items[0].Id);
                Assert.Null(entity.Items[1]);
                Set(dto, "Items", null);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Items" });
                Assert.Null(entity.Items);
            }
        }

        [Fact]
        public void ReverseConversionsArePreparedBeforeWritesAndOmittedConversionsNeverRun()
        {
            using (var assembly = new DynamicAssembly())
            {
                var calls = 0;
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name)
                    .MapConvertedInMemory<int, string>(e => e.Id, "Id", v => v.ToString(),
                        v => { calls++; return int.Parse(v); }, BindingMode.TwoWay));
                assembly.Build();
                var dto = cls.CreateDto(new Entity { Name = "update", Id = 3 });
                var entity = new Entity { Name = "keep", Id = 9 };
                Set(dto, "Id", "invalid");
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Name" });
                Assert.Equal(0, calls);
                entity.Name = "keep";
                Assert.Throws<FormatException>(() => Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Name", "Id" }));
                Assert.Equal("keep", entity.Name);
                Assert.Equal(9, entity.Id);
                Set(dto, "Id", "7");
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "ID", "id" });
                Assert.Equal(2, calls);
                Assert.Equal(7, entity.Id);
            }
        }

        [Fact]
        public void OverlayPresenceUsesEffectiveShapeAndWholeObjectsApplyOverlaysOnce()
        {
            using (var assembly = new DynamicAssembly())
            {
                var calls = 0;
                assembly.DeclareClass<Item>("Item", b => b.Map(e => e.Id));
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Child)
                    .MapConvertedInMemory<string, string>(e => e.Description, "Child.Description",
                        v => v, v => { calls++; return v; }, BindingMode.TwoWay)
                    .Compute("Child.ReadOnly", e => e.Name));
                var plain = assembly.DeclareClass<Entity>("Plain", b => b.Map(e => e.Child));
                assembly.Build();
                var source = new Entity { Child = new Item { Id = 5 }, Description = "new" };
                var dto = cls.CreateDto(source);
                var entity = new Entity { Description = "old", Child = new Item { Id = 9, Name = "keep" } };
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child" });
                Assert.Equal(5, entity.Child.Id);
                Assert.Equal("keep", entity.Child.Name);
                Assert.Equal("new", entity.Description);
                Assert.Equal(1, calls);
                Assert.Throws<ArgumentException>(() => Mapper.MapPartialFromDtoToEntity(plain.CreateDto(source), entity, new[] { "Child.Description" }));
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child.Description" });
                Assert.Equal(2, calls);
                Assert.Throws<ArgumentException>(() => Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child.ReadOnly" }));
                Set(dto, "Child", null);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child" });
                Assert.Null(entity.Child);
                Assert.Equal(2, calls);
            }
        }

        [Fact]
        public void FiniteRecursivePresencePathsDoNotNeedUnboundedPlanCaches()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Item>("Recursive", b => b.Map(e => e.Id).Map(e => e.Child));
                assembly.Build();
                var dto = cls.CreateDto(new Item { Child = new Item { Child = new Item { Id = 5 } } });
                var entity = new Item();
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child.Child.Id" });
                Assert.Equal(5, entity.Child.Child.Id);
                Assert.Equal(0, entity.Id);
            }
        }

        [Fact]
        public void NullConversionsClearNullableTargetsOrFailBeforeWrites()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name)
                    .MapConverted<int?, string>(e => e.Optional, "Optional", v => v.ToString(),
                        v => v == null ? (int?)null : int.Parse(v), BindingMode.TwoWay)
                    .MapConverted<string, string>(e => e.Description, "Description", v => v,
                        v => v ?? "default", BindingMode.TwoWay));
                assembly.Build();
                var dto = cls.CreateDto(new Entity());
                var entity = new Entity { Optional = 3, Description = "old" };
                Set(dto, "Optional", null);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Optional", "Description" });
                Assert.Null(entity.Optional);
                Assert.Equal("default", entity.Description);
            }
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name)
                    .MapConverted<int, object>(e => e.Id, "Boxed", v => (object)v,
                        v => (int)v, BindingMode.TwoWay));
                assembly.Build();
                var dto = cls.CreateDto(new Entity { Name = "new" });
                Set(dto, "Boxed", null);
                var entity = new Entity { Name = "old" };
                Assert.Throws<NullReferenceException>(() => Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Name", "Boxed" }));
                Assert.Equal("old", entity.Name);
            }
        }

        [Fact]
        public void DeepWholeObjectOverlaysRespectTheirOwningEntity()
        {
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass<Item>("Item", b => b.Map(e => e.Id).Map(e => e.Child));
                var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Child)
                    .Map(e => e.Description, "Child.Child.Description"));
                assembly.Build();
                var dto = cls.CreateDto(new Entity
                {
                    Child = new Item { Id = 1, Child = new Item { Id = 2 } },
                    Description = "root overlay"
                });
                var entity = new Entity();
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child" });
                Assert.Equal(2, entity.Child.Child.Id);
                Assert.Equal("root overlay", entity.Description);
                Set(Get(Get(dto, "Child"), "Child"), "Description", "selected");
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child.Child.Description" });
                Assert.Equal("selected", entity.Description);
                Set(Get(dto, "Child"), "Child", null);
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Child" });
                Assert.Equal("selected", entity.Description);
                Assert.NotNull(entity.Child.Child);
            }
        }

        public class ThrowingEntity
        {
            public string Name { get; set; }
            public string Value
            {
                get => throw new NotSupportedException("getter");
                set => throw new NotSupportedException("setter");
            }
            public ThrowingChild Child { get; set; }
        }
        public class ThrowingChild
        {
            public ThrowingChild() { throw new NotSupportedException("constructor"); }
            public string Value { get; set; }
        }

        [Fact]
        public void OmittedGettersAndUserConstructorSetterFailuresRetainTheirContracts()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<ThrowingEntity>("Edit", b => b.Map(e => e.Name)
                    .Map(e => e.Value, mode: BindingMode.OneWayToEntity)
                    .Map(e => e.Child.Value, "ChildValue", BindingMode.OneWayToEntity));
                assembly.Build();
                var dto = cls.CreateDto(new ThrowingEntity { Name = "new" });
                var entity = new ThrowingEntity { Name = "old" };
                Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Name" });
                Assert.Equal("new", entity.Name);
                var setter = Assert.Throws<NotSupportedException>(() => Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "Value" }));
                Assert.Equal("setter", setter.Message);
                var constructor = Assert.Throws<NotSupportedException>(() => Mapper.MapPartialFromDtoToEntity(dto, entity, new[] { "ChildValue" }));
                Assert.Equal("constructor", constructor.Message);
                Assert.Null(entity.Child);
            }
        }
        private sealed class SingleUsePaths : IEnumerable<string>
        {
            private bool _used;
            public IEnumerator<string> GetEnumerator()
            {
                if (_used) throw new InvalidOperationException("Enumerated twice.");
                _used = true;
                yield return "Name";
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Fact]
        public void PresenceIsEnumeratedOnceAndPlansSurviveDisposalAndCollection()
        {
            var reference = ExerciseCollectiblePlan();
            for (var i = 0; i < 20 && reference.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(reference.IsAlive);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ExerciseCollectiblePlan()
        {
            var assembly = new DynamicAssembly();
            var cls = assembly.DeclareClass<Entity>("Edit", b => b.Map(e => e.Name));
            assembly.Build();
            var type = cls.GetBuiltType();
            var dto = cls.CreateDto(new Entity { Name = "update" });
            ((IDisposable)assembly).Dispose();
            Parallel.For(0, 50, i =>
            {
                var entity = new Entity();
                Mapper.MapPartialFromDtoToEntity(dto, entity, new SingleUsePaths());
                Assert.Equal("update", entity.Name);
            });
            return new WeakReference(type);
        }
    }
}
