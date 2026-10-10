using System;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class DtoCreationTests
    {
        [Fact]
        public void CreationReturnsFreshInstancesOfSelectedShape()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<TypedBindingTests.Product>("Dto", b => b.Map(p => p.Id, isPrimaryKey: true));
                assembly.Build();
                var source = new TypedBindingTests.Product { Id = 5 };
                var first = cls.CreateDto(source);
                var second = cls.CreateDto(source);
                Assert.NotSame(first, second);
                Assert.Equal(cls.GetBuiltType(), first.GetType());
                Assert.Equal(new object[] { 5 }, first.GetIds());
                Assert.Throws<ArgumentNullException>(() => cls.CreateDto(null));
                Assert.Throws<ArgumentException>(() => cls.CreateDto(new object()));
                Mapper.MapFromEntityToDto(first, null);
                Mapper.MapFromDtoToEntity(null, source);
            }
        }

        [Fact]
        public void CreationDoesNotBuildOrRecoverFaultedOrDisposedDeclarations()
        {
            var assembly = new DynamicAssembly();
            var cls = assembly.DeclareClass<TypedBindingTests.Product>("Dto", b => b.Map(p => p.Id));
            Assert.Throws<InvalidOperationException>(() => cls.CreateDto(new TypedBindingTests.Product()));
            assembly.Build();
            ((IDisposable)assembly).Dispose();
            Assert.Throws<ObjectDisposedException>(() => cls.CreateDto(new TypedBindingTests.Product()));
            using (var failed = new DynamicAssembly())
            {
                var broken = failed.DeclareClass("Dto", typeof(TypedBindingTests.Product), new[] { new PropertyBinding { EntityProperty = "Missing" } });
                var original = Assert.Throws<MappingValidationException>(() => failed.Build());
                Assert.Same(original, Assert.Throws<InvalidOperationException>(() => broken.CreateDto(new TypedBindingTests.Product())).InnerException);
            }
        }

        public class ThrowingEntity
        {
            public string Name => throw new FormatException("getter failed");
        }

        [Fact]
        public void GetterFailurePropagatesWithoutReflectionWrapping()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<ThrowingEntity>("Dto", b => b.Map(p => p.Name, mode: BindingMode.OneWayToDto));
                assembly.Build();
                Assert.Equal("getter failed", Assert.Throws<FormatException>(() => cls.CreateDto(new ThrowingEntity())).Message);
            }
        }
    }
}
