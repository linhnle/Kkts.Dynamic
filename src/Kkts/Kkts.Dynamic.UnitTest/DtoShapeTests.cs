using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class DtoShapeTests
    {
        public class Item
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Edit { get; set; }
            public Detail Detail { get; set; }
        }
        public class Detail { public int Id { get; set; } }
        public class Parent
        {
            public Item Child { get; set; }
            public List<Item> Items { get; set; }
            public Item[] Array { get; set; }
            public string Title { get; set; }
        }
        private static object Get(object obj, string name) => obj.GetType().GetProperty(name).GetValue(obj);

        [Fact]
        public void NamedShapesMapObjectsAndCollectionsConsistently()
        {
            using (var assembly = new DynamicAssembly())
            {
                var parent = assembly.DeclareClass<Parent>("Parent", b => b
                    .Map(p => p.Child, nestedShape: "Summary")
                    .Map(p => p.Items, nestedShape: "Detail")
                    .Map(p => p.Array, nestedShape: "Summary"));
                var summary = assembly.DeclareClass<Item>("Summary", b => b.Map(p => p.Id, isPrimaryKey: true));
                var detail = assembly.DeclareClass<Item>("Detail", b => b.Map(p => p.Id).Map(p => p.Name));
                var edit = assembly.DeclareClass<Item>("Edit", b => b.Map(p => p.Edit));
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass<Parent>("Parent", b => b.Map(p => p.Child, nestedShape: "Detail")));
                assembly.Build();
                var source = new Parent { Child = new Item { Id = 1, Name = "child" }, Items = new List<Item> { new Item { Id = 2, Name = "item" }, null }, Array = new[] { new Item { Id = 3 }, null } };
                foreach (var dto in new[] { parent.CreateDto(source), parent.BuildSelectorExpression().Compile().DynamicInvoke(source) })
                {
                    Assert.Equal(summary.GetBuiltType(), Get(dto, "Child").GetType());
                    var items = ((IEnumerable)Get(dto, "Items")).Cast<object>().ToArray();
                    Assert.Equal(detail.GetBuiltType(), items[0].GetType());
                    Assert.Null(items[1]);
                    Assert.Equal(new object[] { 1 }, Get(dto, "Child").GetIds());
                    var target = new Parent();
                    Mapper.MapFromDtoToEntity(dto, target);
                    Assert.Equal(1, target.Child.Id);
                    Assert.Null(target.Child.Name);
                    Assert.Equal("item", target.Items[0].Name);
                    Assert.Null(target.Array[1]);
                }
                Assert.Null(edit.GetBuiltType().GetProperty("Id"));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("Missing")]
        [InlineData("Parent")]
        public void InvalidOrAmbiguousShapesFailBeforeEmission(string selected)
        {
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass<Parent>("Parent", b => b.Map(p => p.Child, nestedShape: selected));
                assembly.DeclareClass<Item>("Summary", b => b.Map(p => p.Id));
                assembly.DeclareClass<Item>("Detail", b => b.Map(p => p.Name));
                var report = assembly.Validate();
                Assert.False(report.IsValid);
                Assert.Contains(report.Diagnostics, d => d.DtoPath == "Child" && d.Message.Contains(selected ?? "multiple shapes"));
                Assert.Throws<MappingValidationException>(() => assembly.Build());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OverlaysAreIsolatedAndRetainParentScopedSources(bool reverseOrder)
        {
            using (var assembly = new DynamicAssembly())
            {
                Class first = null, second = null, plain = null;
                Action declareFirst = () => first = assembly.DeclareClass<Parent>("First", b => b.Map(p => p.Child)
                    .Map(p => p.Child.Id, "Child.ExtraId", BindingMode.OneWayToDto)
                    .Map(p => p.Title, "Child.Title"));
                Action declareSecond = () => second = assembly.DeclareClass<Parent>("Second", b => b.Map(p => p.Child)
                    .Map(p => p.Child.Name, "Child.ExtraName", BindingMode.OneWayToDto));
                var baseShape = assembly.DeclareClass<Item>("Item", b => b.Map(p => p.Id, isPrimaryKey: true).Map(p => p.Name));
                if (reverseOrder) { declareSecond(); declareFirst(); } else { declareFirst(); declareSecond(); }
                plain = assembly.DeclareClass<Parent>("Plain", b => b.Map(p => p.Child));
                assembly.Build();
                Assert.Equal(4, assembly.Classes.Length);
                var source = new Parent { Child = new Item { Id = 7, Name = "child" }, Title = "parent" };
                foreach (var value in new[] { first.CreateDto(source), first.BuildSelectorExpression().Compile().DynamicInvoke(source) })
                {
                    var child = Get(value, "Child");
                    Assert.Equal(7, Get(child, "ExtraId"));
                    Assert.Equal("parent", Get(child, "Title"));
                    Assert.Equal(new object[] { 7 }, child.GetIds());
                    child.GetType().GetProperty("Title").SetValue(child, "updated");
                    var target = new Parent();
                    Mapper.MapFromDtoToEntity(value, target);
                    Assert.Equal("updated", target.Title);
                    Assert.Equal(7, target.Child.Id);
                }
                var otherChild = Get(second.CreateDto(source), "Child");
                Assert.Null(otherChild.GetType().GetProperty("ExtraId"));
                Assert.Null(otherChild.GetType().GetProperty("Title"));
                Assert.Equal("child", Get(otherChild, "ExtraName"));
                Assert.Equal(baseShape.GetBuiltType(), Get(plain.CreateDto(source), "Child").GetType());
                Assert.Equal(new[] { "Id", "Name" }, baseShape.GetBuiltType().GetProperties().Select(p => p.Name).OrderBy(n => n));
            }
        }

        [Fact]
        public void DeepOverlaysDoNotMutateRegisteredDescendantShapes()
        {
            using (var assembly = new DynamicAssembly())
            {
                var detail = assembly.DeclareClass<Detail>("Detail", b => b.Map(p => p.Id));
                var item = assembly.DeclareClass<Item>("Item", b => b.Map(p => p.Id).Map(p => p.Detail));
                var parent = assembly.DeclareClass<Parent>("Parent", b => b.Map(p => p.Child)
                    .Map(p => p.Title, "Child.Detail.ExtraTitle"));
                assembly.Build();
                var source = new Parent { Title = "parent", Child = new Item { Id = 4, Detail = new Detail { Id = 5 } } };
                foreach (var dto in new[] { parent.CreateDto(source), parent.BuildSelectorExpression().Compile().DynamicInvoke(source) })
                {
                    var nested = Get(Get(dto, "Child"), "Detail");
                    Assert.Equal(5, Get(nested, "Id"));
                    Assert.Equal("parent", Get(nested, "ExtraTitle"));
                }
                Assert.Null(detail.GetBuiltType().GetProperty("ExtraTitle"));
                Assert.Equal(detail.GetBuiltType(), Get(item.CreateDto(source.Child), "Detail").GetType());
            }
        }

        [Fact]
        public void OverlayConflictsAreDiagnosedWithoutChangingBaseDeclaration()
        {
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass<Item>("Item", b => b.Map(p => p.Id));
                assembly.DeclareClass<Parent>("Parent", b => b.Map(p => p.Child)
                    .Map(p => p.Title, "Child.Id"));
                Assert.Contains(assembly.Validate().Diagnostics, d => d.Code == "OverlayConflict" && d.Message.Contains("Item"));
                Assert.Throws<MappingValidationException>(() => assembly.Build());
            }
        }
    }
}
