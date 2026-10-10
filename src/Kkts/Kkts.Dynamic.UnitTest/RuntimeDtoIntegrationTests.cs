using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class RuntimeDtoIntegrationTests
    {
        public class Category
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
        public class Product
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public Category Category { get; set; }
            public string Notes { get; set; }
        }
        private static object Get(object dto, string name) => dto.GetType().GetProperty(name).GetValue(dto);
        private static void Set(object dto, string name, object value) => dto.GetType().GetProperty(name).SetValue(dto, value);

        [Fact]
        public void SummaryDetailAndEditShareValidatedDefinitionsWithoutSharingOverlays()
        {
            using (var assembly = new DynamicAssembly())
            {
                var categorySummary = assembly.DeclareClass<Category>("CategorySummary", b => b.Map(c => c.Id, isPrimaryKey: true));
                var categoryDetail = assembly.DeclareClass<Category>("CategoryDetail", b => b.Map(c => c.Id, isPrimaryKey: true).Map(c => c.Name));
                var summary = assembly.DeclareClass<Product>("Summary", b => b.Map(p => p.Id, isPrimaryKey: true)
                    .Map(p => p.Category, nestedShape: "CategorySummary")
                    .Compute("Label", p => p.Name + " summary"));
                var detail = assembly.DeclareClass<Product>("Detail", b => b.Map(p => p.Id, isPrimaryKey: true)
                    .Map(p => p.Category, nestedShape: "CategoryDetail")
                    .Map(p => p.Notes, "Category.Notes", BindingMode.OneWayToDto));
                var edit = assembly.DeclareClass<Product>("Edit", b => b
                    .MapConverted<int, string>(p => p.Id, "Key", v => v.ToString(), v => int.Parse(v),
                        BindingMode.TwoWay, isPrimaryKey: true)
                    .Map(p => p.Name)
                    .Map(p => p.Category, nestedShape: "CategoryDetail")
                    .Map(p => p.Notes, "Category.EditNotes")
                    .Compute("Label", p => p.Name + " edit"));
                Assert.True(assembly.Validate().IsValid);
                var invalid = assembly.ValidateClass("Invalid", typeof(Product), new[]
                {
                    new PropertyBinding { EntityProperty = "Unknown", DtoProperty = "Unknown" },
                    new PropertyBinding { EntityProperty = "Category", DtoProperty = "Category" }
                });
                Assert.False(invalid.IsValid);
                Assert.True(invalid.Diagnostics.Count >= 2);
                assembly.Build();

                var source = new Product { Id = 3, Name = "product", Category = new Category { Id = 5, Name = "category" }, Notes = "notes" };
                var summaryDto = summary.CreateDto(source);
                Assert.Equal(categorySummary.GetBuiltType(), Get(summaryDto, "Category").GetType());
                Assert.Equal("product summary", Get(summaryDto, "Label"));
                var detailDto = detail.CreateDto(source);
                var editDto = edit.CreateDto(source);
                Assert.NotEqual(Get(detailDto, "Category").GetType(), Get(editDto, "Category").GetType());
                Assert.Null(categoryDetail.GetBuiltType().GetProperty("Notes"));
                Assert.Null(categoryDetail.GetBuiltType().GetProperty("EditNotes"));
                Assert.Null(Get(detailDto, "Category").GetType().GetProperty("EditNotes"));
                Assert.Equal("notes", Get(Get(editDto, "Category"), "EditNotes"));
                Assert.Equal("3", Assert.Single(DtoObject.GetIds(editDto)));

                var selector = edit.BuildSelectorExpression();
                var projected = selector.Compile().DynamicInvoke(source);
                Assert.Equal(Get(editDto, "Key"), Get(projected, "Key"));
                Assert.Equal(Get(editDto, "Label"), Get(projected, "Label"));
                Assert.Equal(Get(Get(editDto, "Category"), "EditNotes"), Get(Get(projected, "Category"), "EditNotes"));
                var call = Expression.Call(typeof(Queryable), "Select", new[] { typeof(Product), edit.GetBuiltType() },
                    new[] { source }.AsQueryable().Expression, Expression.Quote(selector));
                var query = new[] { source }.AsQueryable().Provider.CreateQuery(call);
                Assert.Equal("3", Get(query.Cast<object>().Single(), "Key"));

                var entity = new Product { Id = 9, Name = "keep", Category = new Category { Id = 8, Name = "existing" }, Notes = "old" };
                Set(editDto, "Name", null);
                Set(Get(editDto, "Category"), "EditNotes", "updated");
                Mapper.MapPartialFromDtoToEntity(editDto, entity, new[] { "Name", "Category.EditNotes" });
                Assert.Null(entity.Name);
                Assert.Equal(9, entity.Id);
                Assert.Equal(8, entity.Category.Id);
                Assert.Equal("updated", entity.Notes);
                Mapper.MapPartialFromDtoToEntity(editDto, entity, new[] { "Key" });
                Assert.Equal(3, entity.Id);
                Set(editDto, "Category", null);
                Mapper.MapPartialFromDtoToEntity(editDto, entity, new[] { "Category" });
                Assert.Null(entity.Category);
                Assert.Equal("updated", entity.Notes);
            }
        }
    }
}
