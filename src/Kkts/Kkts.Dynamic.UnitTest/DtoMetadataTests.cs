using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class DtoMetadataTests
    {
        public class Product
        {
            public int Id { get; set; }
            public int SecondId { get; set; }
            public string ProductName { get; set; }
            public decimal Price { get; set; }
            public string ExternalCode { get; set; }
            public Category Category { get; set; }
            public Category[] Array { get; set; }
            public List<Category> Items { get; set; }
        }

        public class Category
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Editable { get; set; }
        }

        private static DtoFieldMetadata Field(DtoShapeMetadata metadata, string path)
        {
            var result = metadata.ResolveField(path);
            Assert.True(result.Success, result.Diagnostic?.Message);
            Assert.Null(result.Diagnostic);
            Assert.NotNull(result.Field);
            return result.Field;
        }

        private static void AssertLayout(DtoLayoutMetadata layout, HashSet<DtoLayoutMetadata> visited)
        {
            if (!visited.Add(layout)) return;
            var properties = layout.DtoType.GetProperties().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.Equal(properties.Select(p => p.Name), layout.Members.Select(m => m.Name));
            for (var i = 0; i < properties.Length; i++)
            {
                var member = layout.Members[i];
                Assert.Equal(properties[i].PropertyType, member.DtoValueType);
                if (member.NestedLayout != null) AssertLayout(member.NestedLayout, visited);
            }
        }

        [Fact]
        public void PublicSurfaceContainsOnlyImmutablePublicDescriptors()
        {
            var descriptors = new[] { typeof(DtoShapeMetadata), typeof(DtoLayoutMetadata), typeof(DtoMemberMetadata),
                typeof(DtoBindingMetadata), typeof(DtoFieldMetadata), typeof(DtoFieldResolution), typeof(DtoMetadataDiagnostic) };
            foreach (var type in descriptors)
            {
                Assert.True(type.IsSealed);
                Assert.Empty(type.GetConstructors());
                foreach (var property in type.GetProperties())
                {
                    Assert.Null(property.SetMethod);
                    Assert.True(property.PropertyType.IsVisible);
                    Assert.NotEqual(typeof(PropertyBinding), property.PropertyType);
                    Assert.NotEqual(typeof(Class), property.PropertyType);
                    Assert.NotEqual(typeof(DynamicAssembly), property.PropertyType);
                    Assert.False(typeof(Delegate).IsAssignableFrom(property.PropertyType));
                    Assert.False(typeof(Expression).IsAssignableFrom(property.PropertyType));
                }
            }
        }

        [Fact]
        public void ResolvesPublicAliasesGroupsAndCanonicalEntitySpelling()
        {
            var original = new PropertyBinding
            {
                EntityProperty = "productname", DtoProperty = "Name", Mode = BindingMode.TwoWay
            };
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Product>("ProductDto", b => b.Map(original)
                    .Map(p => p.Id, isPrimaryKey: true)
                    .Map(p => p.Category.Name, "CategoryName", BindingMode.OneWayToDto)
                    .Map(p => p.ExternalCode, "External.Code"));
                original.DtoProperty = "Changed";
                assembly.Build();
                original.EntityProperty = "Missing";
                original.Mode = BindingMode.OneWayToEntity;
                var metadata = cls.GetMetadata();
                Assert.Equal("ProductDto", metadata.Name);
                Assert.Equal(typeof(Product), metadata.EntityType);
                Assert.Equal(cls.GetBuiltType(), metadata.DtoType);
                Assert.Same(metadata, cls.GetMetadata());
                Assert.Equal(new[] { "CategoryName", "External", "Id", "Name" }, metadata.Layout.Members.Select(m => m.Name));
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in new[] { "nAmE", "categoryname", "external.code" })
                {
                    var field = Field(metadata, path);
                    Assert.Equal(typeof(Product), field.EntityType);
                    Assert.Equal(typeof(string), field.EntityValueType);
                    Assert.Equal(typeof(string), field.DtoValueType);
                    aliases.Add(field.DtoPath, field.DirectEntityAlias);
                }
                Assert.Equal("ProductName", aliases["Name"]);
                Assert.Equal("Category.Name", aliases["CategoryName"]);
                Assert.Equal("ExternalCode", aliases["External.Code"]);
                var group = Field(metadata, "external");
                Assert.Equal(DtoMemberKind.Grouping, group.Kind);
                Assert.Null(group.Binding);
                Assert.Null(group.EntityPath);
                Assert.Null(group.EntityValueType);
                Assert.Null(group.DirectEntityAlias);
                Assert.False(group.HasForwardMapping);
                Assert.False(group.HasReverseMapping);
                Assert.NotNull(group.NestedLayout);
                var list = Assert.IsAssignableFrom<IList<DtoMemberMetadata>>(metadata.Layout.Members);
                Assert.Throws<NotSupportedException>(() => list.Clear());
                var keys = Assert.IsAssignableFrom<IList<DtoFieldMetadata>>(metadata.PrimaryKeys);
                Assert.Throws<NotSupportedException>(() => keys.Clear());
                Assert.True(Field(metadata, "CategoryName").HasForwardMapping);
                Assert.False(Field(metadata, "CategoryName").HasReverseMapping);
            }
        }

        [Fact]
        public void ClassifiesAllBindingsAndDoesNotInferTransformedAliases()
        {
            var calls = 0;
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass<Category>("Category", b => b.Map(c => c.Id));
                var cls = assembly.DeclareClass<Product>("Product", b => b
                    .Map(p => p.ProductName, "Name")
                    .Map(p => p.Category)
                    .Map(p => p.Array)
                    .Map(p => p.Items)
                    .MapConverted<decimal, string>(p => p.Price, "PriceText", v => v.ToString(),
                        v => decimal.Parse(v), BindingMode.TwoWay)
                    .MapConverted<string, string>(p => p.ExternalCode, "Identity", v => v)
                    .Compute("Label", p => p.ProductName + p.Id)
                    .ComputeInMemory("InMemory", p => { calls++; return p.ProductName; })
                    .MapConvertedInMemory<int, string>(p => p.Id, "InMemoryKey", v => { calls++; return v.ToString(); })
                    .Map(new PropertyBinding
                    {
                        EntityProperty = "SecondId", DtoProperty = "Mixed", DtoValueType = typeof(string),
                        ToDtoExpression = (Expression<Func<int, string>>)(v => v.ToString()),
                        ToEntityInMemory = (Func<string, int>)int.Parse, Mode = BindingMode.TwoWay
                    }));
                assembly.Build();
                var metadata = cls.GetMetadata();
                AssertLayout(metadata.Layout, new HashSet<DtoLayoutMetadata>());
                Assert.Equal(0, calls);
                Assert.Equal(DtoBindingKind.Direct, Field(metadata, "Name").Binding.Kind);
                Assert.Equal(DtoBindingKind.MappedObject, Field(metadata, "Category").Binding.Kind);
                Assert.Equal(DtoBindingKind.MappedArray, Field(metadata, "Array").Binding.Kind);
                Assert.Equal(DtoBindingKind.MappedCollection, Field(metadata, "Items").Binding.Kind);
                Assert.Equal(DtoForwardExecutionKind.NestedMapping, Field(metadata, "Category").ForwardExecutionKind);
                Assert.Equal(DtoBindingKind.Computed, Field(metadata, "Label").Binding.Kind);
                Assert.Null(Field(metadata, "Label").EntityPath);
                Assert.Null(Field(metadata, "Label").EntityValueType);
                Assert.Equal(DtoForwardExecutionKind.Expression, Field(metadata, "Label").ForwardExecutionKind);
                foreach (var path in new[] { "PriceText", "Identity", "Mixed", "InMemoryKey" })
                {
                    Assert.Equal(DtoBindingKind.Converted, Field(metadata, path).Binding.Kind);
                    Assert.Null(Field(metadata, path).DirectEntityAlias);
                }
                Assert.Equal("Price", Field(metadata, "PriceText").EntityPath);
                Assert.Equal(typeof(decimal), Field(metadata, "PriceText").EntityValueType);
                Assert.Equal(typeof(string), Field(metadata, "PriceText").DtoValueType);
                Assert.Equal(DtoForwardExecutionKind.Expression, Field(metadata, "Mixed").ForwardExecutionKind);
                Assert.True(Field(metadata, "Mixed").HasReverseMapping);
                Assert.Equal(DtoForwardExecutionKind.InMemory, Field(metadata, "InMemory").ForwardExecutionKind);
                Assert.Equal(DtoForwardExecutionKind.InMemory, Field(metadata, "InMemoryKey").ForwardExecutionKind);
                Assert.Equal(0, calls);
                Assert.Throws<InvalidOperationException>(() => cls.BuildSelectorExpression());
            }
        }

        [Fact]
        public void KeysKeepDtoTypesConfiguredOrderAndStableTies()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Product>("Keys", b => b
                    .Map(p => p.SecondId, "Z", isPrimaryKey: true, primaryKeyOrder: 2)
                    .MapConverted<int, string>(p => p.Id, "A", v => v.ToString(), isPrimaryKey: true, primaryKeyOrder: 2)
                    .Map(p => p.Price));
                assembly.Build();
                var metadata = cls.GetMetadata();
                Assert.Equal(new[] { "A", "Z" }, metadata.PrimaryKeys.Select(k => k.DtoPath));
                Assert.Equal(new[] { typeof(string), typeof(int) }, metadata.PrimaryKeys.Select(k => k.DtoValueType));
                Assert.All(metadata.PrimaryKeys, k => Assert.Equal(2, k.Binding.PrimaryKeyOrder));
                Assert.Equal(new object[] { "7", 8 }, cls.CreateDto(new Product { Id = 7, SecondId = 8 }).GetIds());
                Assert.Null(Field(metadata, "Price").Binding.PrimaryKeyOrder);
            }
        }

        [Fact]
        public void SnapshotDoesNotChangeExistingUnusualGeneratedMemberNames()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Product>("Names", b => b.Map(new PropertyBinding
                    { EntityProperty = "Id", DtoProperty = " Key ", Mode = BindingMode.TwoWay, IsPrimaryKey = true })
                    .Map(p => p.ProductName, "Display Name"));
                assembly.Build();
                var metadata = cls.GetMetadata();
                Assert.Equal(" Key ", Assert.Single(metadata.PrimaryKeys).DtoPath);
                Assert.Equal(new object[] { 3 }, cls.CreateDto(new Product { Id = 3 }).GetIds());
                Assert.Equal("ProductName", Field(metadata, "Display Name").DirectEntityAlias);
                Assert.Equal("InvalidDtoPath", metadata.ResolveField(" Key ").Diagnostic.Code);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EffectiveOverlaysRetainTheirSourceAndDoNotLeak(bool reverseOrder)
        {
            using (var assembly = new DynamicAssembly())
            {
                var detail = assembly.DeclareClass<DtoShapeTests.Detail>("Detail", b => b.Map(e => e.Id));
                var item = assembly.DeclareClass<DtoShapeTests.Item>("Item", b => b
                    .Map(e => e.Id, isPrimaryKey: true).Map(e => e.Name).Map(e => e.Detail));
                Class first = null, second = null;
                Action declareFirst = () => first = assembly.DeclareClass<DtoShapeTests.Parent>("First", b => b.Map(e => e.Child)
                    .Map(e => e.Title, "Child.Title").Map(e => e.Title, "Child.Detail.ExtraTitle", BindingMode.OneWayToDto));
                Action declareSecond = () => second = assembly.DeclareClass<DtoShapeTests.Parent>("Second", b => b.Map(e => e.Child)
                    .Map(e => e.Child.Name, "Child.ExtraName", BindingMode.OneWayToDto));
                if (reverseOrder) { declareSecond(); declareFirst(); } else { declareFirst(); declareSecond(); }
                var plain = assembly.DeclareClass<DtoShapeTests.Parent>("Plain", b => b.Map(e => e.Child));
                assembly.Build();
                var metadata = first.GetMetadata();
                AssertLayout(metadata.Layout, new HashSet<DtoLayoutMetadata>());
                Assert.Equal("Title", Field(metadata, "Child.Title").DirectEntityAlias);
                Assert.Equal(typeof(DtoShapeTests.Parent), Field(metadata, "Child.Title").Binding.EntityType);
                Assert.Equal("First", Field(metadata, "Child.Title").Binding.DeclaringShapeName);
                Assert.Equal("Title", Field(metadata, "Child.Detail.ExtraTitle").EntityPath);
                Assert.Equal("Child.Id", Field(metadata, "Child.Id").EntityPath);
                Assert.True(Field(metadata, "Child.Id").Binding.IsPrimaryKey);
                Assert.Empty(metadata.PrimaryKeys);
                var child = Field(metadata, "Child");
                Assert.Same(item.GetMetadata(), child.NestedShape);
                Assert.NotEqual(item.GetBuiltType(), child.NestedLayout.DtoType);
                Assert.Equal(new[] { "Detail", "Id", "Name", "Title" }, child.NestedLayout.Members.Select(m => m.Name));
                Assert.Equal(first.GetBuiltType().GetProperty("Child").PropertyType, child.NestedLayout.DtoType);
                Assert.Equal("Child.Name", Field(second.GetMetadata(), "Child.ExtraName").EntityPath);
                Assert.False(second.GetMetadata().ResolveField("Child.Title").Success);
                Assert.False(item.GetMetadata().ResolveField("Title").Success);
                Assert.False(detail.GetMetadata().ResolveField("ExtraTitle").Success);
                Assert.Same(item.GetMetadata().Layout, Field(plain.GetMetadata(), "Child").NestedLayout);
                var source = new DtoShapeTests.Parent { Title = "parent", Child = new DtoShapeTests.Item
                    { Id = 7, Name = "child", Detail = new DtoShapeTests.Detail { Id = 8 } } };
                Assert.Equal(child.NestedLayout.DtoType, first.CreateDto(source).GetType().GetProperty("Child").GetValue(first.CreateDto(source)).GetType());
            }
        }

        [Fact]
        public void SelectedShapesAndInheritedDirectionsAreExact()
        {
            using (var assembly = new DynamicAssembly())
            {
                var summary = assembly.DeclareClass<Category>("Summary", b => b.Map(c => c.Id)
                    .Map(c => c.Editable, mode: BindingMode.OneWayToEntity));
                assembly.DeclareClass<Category>("Detail", b => b.Map(c => c.Name));
                var parent = assembly.DeclareClass<Product>("Parent", b => b
                    .Map(p => p.Category, mode: BindingMode.OneWayToDto, nestedShape: "Summary"));
                var reverse = assembly.DeclareClass<Product>("Reverse", b => b
                    .Map(p => p.Category, mode: BindingMode.OneWayToEntity, nestedShape: "Summary")
                    .Map(p => p.ExternalCode, "Category.Overlay", BindingMode.OneWayToDto));
                assembly.Build();
                var metadata = parent.GetMetadata();
                Assert.Same(summary.GetMetadata(), Field(metadata, "Category").NestedShape);
                Assert.False(metadata.ResolveField("Category.Name").Success);
                Assert.True(Field(metadata, "Category.Id").HasForwardMapping);
                Assert.False(Field(metadata, "Category.Id").HasReverseMapping);
                var editable = Field(metadata, "Category.Editable");
                Assert.False(editable.HasForwardMapping);
                Assert.False(editable.HasReverseMapping);
                Assert.Null(editable.DirectEntityAlias);
                Assert.Equal(BindingMode.OneWayToEntity, editable.Binding.Mode);
                Assert.Equal(DtoForwardExecutionKind.None, editable.ForwardExecutionKind);
                Assert.False(Field(reverse.GetMetadata(), "Category.Id").HasForwardMapping);
                Assert.True(Field(reverse.GetMetadata(), "Category.Id").HasReverseMapping);
                Assert.Equal("ExternalCode", Field(reverse.GetMetadata(), "Category.Overlay").DirectEntityAlias);
            }
        }

        [Fact]
        public void CollectionsExposeElementBoundariesAndActualTypes()
        {
            using (var assembly = new DynamicAssembly())
            {
                var summary = assembly.DeclareClass<Category>("Summary", b => b.Map(c => c.Id));
                var detail = assembly.DeclareClass<Category>("Detail", b => b.Map(c => c.Name));
                var cls = assembly.DeclareClass<Product>("Collections", b => b
                    .Map(p => p.Array, nestedShape: "Summary").Map(p => p.Items, nestedShape: "Detail"));
                assembly.Build();
                var metadata = cls.GetMetadata();
                var array = Field(metadata, "Array");
                Assert.Equal(typeof(Category[]), array.EntityValueType);
                Assert.Equal(summary.GetBuiltType().MakeArrayType(), array.DtoValueType);
                Assert.Empty(array.CollectionBoundaries);
                Assert.Equal(typeof(List<Category>), Field(metadata, "Items").EntityValueType);
                Assert.Equal(typeof(IEnumerable<>).MakeGenericType(detail.GetBuiltType()), Field(metadata, "Items").DtoValueType);
                var leaf = Field(metadata, "items.name");
                Assert.Equal("Items.Name", leaf.DtoPath);
                Assert.Equal("Items.Name", leaf.EntityPath);
                Assert.Equal(typeof(string), leaf.DtoValueType);
                Assert.Equal(new[] { "Items" }, leaf.CollectionBoundaries);
                Assert.Null(leaf.DirectEntityAlias);
                Assert.False(metadata.ResolveField("Items.Id").Success);
                Assert.Null(Field(metadata, "Array.Id").DirectEntityAlias);
            }
        }

        public class Recursive
        {
            public int Id { get; set; }
            public Recursive Child { get; set; }
            public List<Recursive> Items { get; set; }
        }
        public class A { public B Next { get; set; } public int Id { get; set; } }
        public class B { public A Next { get; set; } }

        [Fact]
        public void RecursiveMetadataIsFiniteAndFinitePathsAreIndependentOfSelectors()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Recursive>("Recursive", b => b.Map(e => e.Id).Map(e => e.Child).Map(e => e.Items));
                assembly.Build();
                var metadata = cls.GetMetadata();
                AssertLayout(metadata.Layout, new HashSet<DtoLayoutMetadata>());
                Assert.Equal(3, metadata.Layout.Members.Count);
                Assert.Same(metadata.Layout, Field(metadata, "Child").NestedLayout);
                Assert.Same(metadata, Field(metadata, "Child").NestedShape);
                Assert.Equal("Child.Child.Id", Field(metadata, "child.CHILD.id").DirectEntityAlias);
                Assert.Equal(new[] { "Items", "Items.Items" }, Field(metadata, "Items.Items.Id").CollectionBoundaries);
                Assert.Null(Field(metadata, "Items.Items.Id").DirectEntityAlias);
                Assert.False(metadata.ResolveField("Child.Child.Missing").Success);
                Assert.Throws<InvalidOperationException>(() => cls.BuildSelectorExpression());
                Assert.True(metadata.ResolveField("Child.Child.Id").Success);
            }
            using (var assembly = new DynamicAssembly())
            {
                var a = assembly.DeclareClass<A>("A", b => b.Map(e => e.Id).Map(e => e.Next));
                var b = assembly.DeclareClass<B>("B", bindings => bindings.Map(e => e.Next));
                assembly.Build();
                Assert.Same(a.GetMetadata().Layout, Field(b.GetMetadata(), "Next").NestedLayout);
                Assert.Equal("Next.Next.Id", Field(a.GetMetadata(), "next.next.id").DirectEntityAlias);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(".Name")]
        [InlineData("Name.")]
        [InlineData("External..Code")]
        [InlineData(" Name")]
        [InlineData("Name ")]
        [InlineData("*")]
        [InlineData("Items[0].Name")]
        public void InvalidPathsProduceExclusiveActionableDiagnostics(string path)
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Product>("Paths", b => b.Map(p => p.ProductName, "Name"));
                assembly.Build();
                var result = cls.GetMetadata().ResolveField(path);
                Assert.False(result.Success);
                Assert.Null(result.Field);
                Assert.Equal("InvalidDtoPath", result.Diagnostic.Code);
                Assert.Equal("Paths", result.Diagnostic.ShapeName);
                Assert.Equal(path, result.Diagnostic.DtoPath);
                Assert.False(string.IsNullOrEmpty(result.Diagnostic.SuggestedFix));
            }
        }

        [Fact]
        public void UnknownAndUnconfiguredDescendantsHaveDistinctDiagnostics()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Product>("Paths", b => b.Map(p => p.Category));
                assembly.Build();
                var metadata = cls.GetMetadata();
                Assert.Equal(DtoBindingKind.Direct, Field(metadata, "Category").Binding.Kind);
                var unknown = metadata.ResolveField("Missing").Diagnostic;
                Assert.Equal("UnknownDtoField", unknown.Code);
                Assert.Equal("Missing", unknown.FailedSegment);
                Assert.Equal("NoNestedShape", metadata.ResolveField("Category.Name").Diagnostic.Code);
            }
        }

        [Fact]
        public void MetadataReadDoesNotChangeVirtualMappingNullsOrPartialUpdates()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<MappingRegressionTests.DispatchEntity>("Dispatch", b => b
                    .Map(e => e.Child.Id, "Fields.Id"));
                assembly.Build();
                var metadata = cls.GetMetadata();
                var source = new MappingRegressionTests.DerivedDispatchEntity
                    { ChildValue = new MappingRegressionTests.DerivedDispatchChild() };
                Assert.Equal("Child.Id", Field(metadata, "Fields.Id").DirectEntityAlias);
                var dto = cls.CreateDto(source);
                var group = dto.GetType().GetProperty("Fields").GetValue(dto);
                Assert.Equal(42, group.GetType().GetProperty("Id").GetValue(group));
                Mapper.MapFromEntityToDto(dto, new MappingRegressionTests.DispatchEntity());
                Assert.Equal(42, group.GetType().GetProperty("Id").GetValue(group));
                var target = new MappingRegressionTests.DerivedDispatchEntity
                    { ChildValue = new MappingRegressionTests.DerivedDispatchChild() };
                Mapper.MapPartialFromDtoToEntity(dto, target, new[] { "Fields.Id" });
                Assert.Equal(42, ((MappingRegressionTests.DerivedDispatchChild)target.ChildValue).LastId);
                Assert.Same(metadata, DtoShapeMetadata.GetForType(dto.GetType()));
                Assert.NotNull(cls.BuildSelectorExpression());
            }
        }

        [Fact]
        public void ReadingMetadataPreservesAttributesConversionNullsAndSelectorBehavior()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<TransformationTests.Entity>("Compatibility", b => b
                    .Map(e => e.Id)
                    .MapConverted<string, string>(e => e.Child.First, "Name", value => value ?? "null"));
                assembly.Build();
                var metadata = cls.GetMetadata();
                Assert.Equal(DtoBindingKind.Direct, Field(metadata, "Id").Binding.Kind);
                Assert.Equal(DtoBindingKind.Converted, Field(metadata, "Name").Binding.Kind);
                var attribute = cls.GetBuiltType().GetProperty("Id")
                    .GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>();
                Assert.Equal("Identifier", attribute.DisplayName);
                var source = new TransformationTests.Entity { Id = 5, Child = new TransformationTests.Entity() };
                var dto = cls.CreateDto(source);
                Assert.Equal("null", dto.GetType().GetProperty("Name").GetValue(dto));
                Mapper.MapFromEntityToDto(dto, new TransformationTests.Entity());
                Assert.Equal("null", dto.GetType().GetProperty("Name").GetValue(dto));
                var projected = cls.BuildSelectorExpression().Compile().DynamicInvoke(new TransformationTests.Entity());
                Assert.Null(projected.GetType().GetProperty("Name").GetValue(projected));
                Assert.Same(metadata, cls.GetMetadata());
                Assert.Equal("Child.First", Field(metadata, "Name").EntityPath);
                Assert.Null(Field(metadata, "Name").DirectEntityAlias);
            }
        }

        [Fact]
        public void NestedInMemoryFieldDoesNotMakeItsParentExpressionCompatible()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Product>("Parent", b => b.Map(p => p.Category));
                assembly.DeclareClass<Category>("Child", b => b.ComputeInMemory("Name", c => c.Name));
                assembly.Build();
                var metadata = cls.GetMetadata();
                Assert.Equal(DtoForwardExecutionKind.NestedMapping, Field(metadata, "Category").ForwardExecutionKind);
                var child = Field(metadata, "Category.Name");
                Assert.Equal(DtoForwardExecutionKind.InMemory, child.ForwardExecutionKind);
                Assert.Null(child.DirectEntityAlias);
                Assert.Null(child.EntityPath);
                var error = Assert.Throws<InvalidOperationException>(() => cls.BuildSelectorExpression());
                Assert.Contains("Category.Name", error.Message);
                Assert.True(metadata.ResolveField("Category.Name").Success);
            }
        }
    }
}
