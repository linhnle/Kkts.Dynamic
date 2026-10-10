using System;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class TypedBindingTests
    {
        public class Product
        {
            public int Id { get; set; }
            public string ProductName { get; set; }
            public double Price { get; set; }
            public int Field;
            public Product Child { get; set; }
            public static int StaticId => 1;
            public int this[int index] => index;
        }

        private static object Get(object dto, string name) => dto.GetType().GetProperty(name).GetValue(dto);

        [Fact]
        public void TypedBindingsMatchStringsInBothDirectionsAndSelectors()
        {
            using (var assembly = new DynamicAssembly())
            {
                var typed = assembly.DeclareClass<Product>("Typed", b => b
                    .Map(p => p.Id, mode: BindingMode.OneWayToDto, isPrimaryKey: true, primaryKeyOrder: 1)
                    .Map(p => p.ProductName, "Name", isPrimaryKey: true, primaryKeyOrder: 0)
                    .Map(p => p.Field)
                    .Map(p => p.Child.Price, "Price")
                    .Map(p => p.Price, "ReversePrice", BindingMode.OneWayToEntity));
                var strings = assembly.DeclareClass("Strings", typeof(Product), new[]
                {
                    new PropertyBinding { EntityProperty = "Id", Mode = BindingMode.OneWayToDto, IsPrimaryKey = true, PrimaryKeyOrder = 1 },
                    new PropertyBinding { EntityProperty = "ProductName", DtoProperty = "Name", Mode = BindingMode.TwoWay, IsPrimaryKey = true },
                    new PropertyBinding { EntityProperty = "Field", Mode = BindingMode.TwoWay },
                    new PropertyBinding { EntityProperty = "Child.Price", DtoProperty = "Price", Mode = BindingMode.TwoWay },
                    new PropertyBinding { EntityProperty = "Price", DtoProperty = "ReversePrice", Mode = BindingMode.OneWayToEntity }
                });
                assembly.Build();
                var source = new Product { Id = 7, ProductName = "Phone", Field = 3, Price = 4, Child = new Product { Price = 20.5 } };
                var left = typed.CreateDto(source);
                var right = strings.CreateDto(source);
                var projected = typed.BuildSelectorExpression().Compile().DynamicInvoke(source);
                foreach (var name in new[] { "Id", "Name", "Field", "Price", "ReversePrice" })
                {
                    Assert.Equal(Get(right, name), Get(left, name));
                    Assert.Equal(Get(left, name), Get(projected, name));
                }
                Assert.Equal(new object[] { "Phone", 7 }, left.GetIds());
                var target = new Product { Id = 99 };
                Mapper.MapFromDtoToEntity(left, target);
                Assert.Equal(99, target.Id);
                Assert.Equal("Phone", target.ProductName);
                Assert.Equal(3, target.Field);
                Assert.Equal(20.5, target.Child.Price);
                Assert.Equal(0, target.Price);
            }
        }

        [Fact]
        public void DefaultNestedPathAndBoxingKeepActualMemberTypes()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Product>("Dto", b => b.Map<object>(p => p.Id).Map(p => p.Child.Price));
                assembly.Build();
                var dto = cls.CreateDto(new Product { Id = 5, Child = new Product { Price = 6 } });
                Assert.Equal(typeof(int), dto.GetType().GetProperty("Id").PropertyType);
                Assert.Equal(5, Get(dto, "Id"));
                Assert.Equal(6d, Get(Get(dto, "Child"), "Price"));
            }
        }

        [Fact]
        public void UnsupportedExpressionsNeverRegisterAClass()
        {
            var captured = new Product();
            Expression<Func<Product, object>>[] invalid =
            {
                p => p.Id + 1, p => p.ProductName.ToUpper(), p => p[0],
                p => Product.StaticId, p => captured.Id, p => (long)p.Id,
                p => ((Product)(object)p).Id, p => p
            };
            using (var assembly = new DynamicAssembly())
            {
                foreach (var expression in invalid)
                {
                    var error = Assert.Throws<ArgumentException>(() => assembly.DeclareClass<Product>("Dto", b => b.Map(expression)));
                    Assert.Contains("computed", error.Message);
                    Assert.Empty(assembly.Classes);
                }
                Assert.Throws<ArgumentNullException>(() => assembly.DeclareClass<Product>("Dto", b => b.Map<int>(null)));
                Assert.Throws<ArgumentNullException>(() => assembly.DeclareClass<Product>("Dto", null));
                Assert.Empty(assembly.Classes);
            }
        }

        [Fact]
        public void BuilderSnapshotsAndEquivalentRedeclarationsRemainStable()
        {
            using (var assembly = new DynamicAssembly())
            {
                BindingBuilder<Product> captured = null;
                var binding = new PropertyBinding { EntityProperty = "Id" };
                var cls = assembly.DeclareClass<Product>("Dto", b => { captured = b; b.Map(binding); });
                captured.Map(p => p.Price);
                binding.EntityProperty = "Price";
                Assert.Same(cls, assembly.DeclareClass("Dto", typeof(Product), new[] { new PropertyBinding { EntityProperty = "Id", DtoProperty = "Id" } }));
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass<Product>("Dto", b => b.Map(p => p.Price)));
                Assert.Throws<InvalidOperationException>(() => assembly.DeclareClass<Product>("Failed", b => { b.Map(p => p.Id); throw new InvalidOperationException(); }));
                Assert.Single(assembly.Classes);
                assembly.Build();
                Assert.Single(cls.GetBuiltType().GetProperties());
                Assert.Equal(8, Get(cls.CreateDto(new Product { Id = 8 }), "Id"));
            }
        }

        [Fact]
        public void ReadOnlyAndNestedValueTypeRestrictionsMatchStringBindings()
        {
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass<Accessor>("Dto", b => b.Map(p => p.ReadOnly, mode: BindingMode.OneWayToDto));
                assembly.Build();
            }
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass<Accessor>("Dto", b => b.Map(p => p.ReadOnly));
                Assert.Throws<MappingValidationException>(() => assembly.Build());
            }
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass<Accessor>("Dto", b => b.Map(p => p.Value.X));
                Assert.Throws<MappingValidationException>(() => assembly.Build());
            }
        }

        public struct Coordinates { public int X; }
        public class Accessor
        {
            public int ReadOnly => 7;
            public Coordinates Value { get; set; }
        }

        [Fact]
        public void ReadmeQuickStartCreatesAndUpdatesProduct()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Product>("ProductDto", b => b
                    .Map(p => p.Id, mode: BindingMode.OneWayToDto, isPrimaryKey: true)
                    .Map(p => p.ProductName, "Name")
                    .Map(p => p.Price));
                assembly.Build();
                var product = new Product { Id = 1, ProductName = "Phone", Price = 20.5 };
                var dto = cls.CreateDto(product);
                Assert.Equal("Phone", Get(dto, "Name"));
                Assert.Equal(new object[] { 1 }, dto.GetIds());
                dto.GetType().GetProperty("Name").SetValue(dto, "Updated phone");
                Mapper.MapFromDtoToEntity(dto, product);
                Assert.Equal("Updated phone", product.ProductName);
                Assert.Equal(1, product.Id);
            }
        }
    }
}
