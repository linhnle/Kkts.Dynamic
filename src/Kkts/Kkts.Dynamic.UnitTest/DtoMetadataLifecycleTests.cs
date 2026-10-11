using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class DtoMetadataLifecycleTests
    {
        [Fact]
        public void BuildFaultsAndDisposalHaveExplicitMetadataBoundaries()
        {
            var assembly = new DynamicAssembly();
            var cls = assembly.DeclareClass<DtoMetadataTests.Product>("Product", b => b.Map(p => p.Id));
            Assert.Contains("Build", Assert.Throws<InvalidOperationException>(() => cls.GetMetadata()).Message);
            assembly.Build();
            var metadata = cls.GetMetadata();
            var type = cls.GetBuiltType();
            assembly.Build();
            Assert.Same(metadata, cls.GetMetadata());
            ((IDisposable)assembly).Dispose();
            Assert.Throws<ObjectDisposedException>(() => cls.GetMetadata());
            Assert.Same(metadata, DtoShapeMetadata.GetForType(type));
            Assert.True(metadata.ResolveField("Id").Success);
            Assert.True(DtoShapeMetadata.TryGetForType(type, out var found, out var diagnostic));
            Assert.Same(metadata, found);
            Assert.Null(diagnostic);

            using (var failed = new DynamicAssembly())
            {
                var invalid = failed.DeclareClass<DtoMetadataTests.Product>("Invalid", b => b.Map(new PropertyBinding
                    { EntityProperty = "Missing", Mode = BindingMode.TwoWay }));
                Assert.Throws<MappingValidationException>(() => failed.Build());
                var error = Assert.Throws<InvalidOperationException>(() => invalid.GetMetadata());
                Assert.IsType<MappingValidationException>(error.InnerException);
            }
        }

        [Fact]
        public void LateFailureAfterTypeCreationDoesNotPublishAnyShape()
        {
            using (var assembly = new DynamicAssembly())
            {
                var good = assembly.DeclareClass<DtoMetadataTests.Product>("Good", b => b.Map(p => p.Id));
                Class bad = null;
                Type goodType = null, badType = null;
                var body = new CaptureThenUnboundExpression(() =>
                {
                    goodType = good.GetBuiltType();
                    badType = bad.GetBuiltType();
                    Assert.Throws<InvalidOperationException>(() => good.GetMetadata());
                    Assert.False(DtoShapeMetadata.TryGetForType(goodType, out _, out _));
                });
                var expression = Expression.Lambda<Func<DtoMetadataTests.Product, string>>(body,
                    Expression.Parameter(typeof(DtoMetadataTests.Product), "p"));
                bad = assembly.DeclareClass<DtoMetadataTests.Product>("Bad", b => b.Map(new PropertyBinding
                    { DtoProperty = "Label", ComputedExpression = expression, Mode = BindingMode.OneWayToDto }));
                Assert.True(assembly.Validate().IsValid);
                Assert.Throws<InvalidOperationException>(() => assembly.Build());
                Assert.NotNull(goodType);
                Assert.NotNull(badType);
                foreach (var type in new[] { goodType, badType })
                {
                    Assert.False(DtoShapeMetadata.TryGetForType(type, out var metadata, out var diagnostic));
                    Assert.Null(metadata);
                    Assert.Equal("MetadataUnavailable", diagnostic.Code);
                    Assert.Throws<ArgumentException>(() => DtoShapeMetadata.GetForType(type));
                }
                Assert.NotNull(Assert.Throws<InvalidOperationException>(() => good.GetMetadata()).InnerException);
                Assert.NotNull(Assert.Throws<InvalidOperationException>(() => bad.GetMetadata()).InnerException);
            }
        }

        private sealed class CaptureThenUnboundExpression : Expression
        {
            private readonly Action _capture;
            internal CaptureThenUnboundExpression(Action capture) { _capture = capture; }
            public override Type Type => typeof(string);
            public override ExpressionType NodeType => ExpressionType.Extension;
            public override bool CanReduce => true;
            public override Expression Reduce()
            {
                _capture();
                return Expression.Parameter(typeof(string), "unbound");
            }
        }

        [Fact]
        public void ExactTypeLookupDistinguishesDeclaredAndContextualTypes()
        {
            using (var assembly = new DynamicAssembly())
            {
                var summary = assembly.DeclareClass<DtoMetadataTests.Category>("Summary", b => b.Map(c => c.Id));
                var detail = assembly.DeclareClass<DtoMetadataTests.Category>("Detail", b => b.Map(c => c.Name));
                var parent = assembly.DeclareClass<DtoMetadataTests.Product>("Parent", b => b
                    .Map(p => p.Category, nestedShape: "Summary")
                    .Map(p => p.ExternalCode, "Category.Code")
                    .Map(p => p.ProductName, "External.Name"));
                assembly.Build();
                Assert.Equal("Summary", DtoShapeMetadata.GetForType(summary.GetBuiltType()).Name);
                Assert.Equal("Detail", DtoShapeMetadata.GetForType(detail.GetBuiltType()).Name);
                foreach (var type in new[] { parent.GetBuiltType().GetProperty("Category").PropertyType,
                    parent.GetBuiltType().GetProperty("External").PropertyType })
                {
                    Assert.False(DtoShapeMetadata.TryGetForType(type, out var metadata, out var diagnostic));
                    Assert.Null(metadata);
                    Assert.Equal("ContextualDtoType", diagnostic.Code);
                    Assert.Contains("root", diagnostic.SuggestedFix);
                    Assert.Throws<ArgumentException>(() => DtoShapeMetadata.GetForType(type));
                }
            }
            Assert.False(DtoShapeMetadata.TryGetForType(typeof(DtoMetadataTests.Product), out var missing, out var failure));
            Assert.Null(missing);
            Assert.Equal("MetadataUnavailable", failure.Code);
            Assert.Throws<ArgumentException>(() => DtoShapeMetadata.GetForType(typeof(string)));
            Assert.Throws<ArgumentNullException>(() => DtoShapeMetadata.GetForType(null));
            Assert.Throws<ArgumentNullException>(() => DtoShapeMetadata.TryGetForType(null, out _, out _));
        }

        [Fact]
        public void ConcurrentReadersAndTypeLookupSurviveBuilderDisposal()
        {
            var assembly = new DynamicAssembly();
            assembly.DeclareClass<DtoMetadataTests.Category>("Category", b => b.Map(c => c.Id).Map(c => c.Name));
            var cls = assembly.DeclareClass<DtoMetadataTests.Product>("Product", b => b.Map(p => p.Id, isPrimaryKey: true)
                .Map(p => p.Category).Map(p => p.ExternalCode, "Category.Overlay"));
            assembly.Build();
            var metadata = cls.GetMetadata();
            var type = cls.GetBuiltType();
            ((IDisposable)assembly).Dispose();
            Parallel.For(0, 200, i =>
            {
                Assert.Same(metadata, DtoShapeMetadata.GetForType(type));
                Assert.Equal(new[] { "Category", "Id" }, metadata.Layout.Members.Select(m => m.Name));
                Assert.Equal("Category.Name", metadata.ResolveField("category.name").Field.DirectEntityAlias);
                Assert.Equal("ExternalCode", metadata.ResolveField("CATEGORY.overlay").Field.DirectEntityAlias);
                Assert.Equal("UnknownDtoField", metadata.ResolveField("Category.Missing").Diagnostic.Code);
                Assert.Single(metadata.PrimaryKeys);
            });
        }

        [Fact]
        public void WeakDiscoveryDoesNotRootOrdinaryTransformedOverlayOrRecursiveTypes()
        {
            foreach (var variant in new[] { "ordinary", "transformed", "overlay", "recursive" })
            {
                var reference = CreateCollectibleMetadata(variant, out var snapshot);
                AssertRetainedThenRelease(reference, ref snapshot);
                Collect(reference);
                Assert.False(reference.IsAlive, variant);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateCollectibleMetadata(string variant, out DtoShapeMetadata snapshot)
        {
            using (var assembly = new DynamicAssembly())
            {
                Class cls;
                if (variant == "recursive")
                    cls = assembly.DeclareClass<DtoMetadataTests.Recursive>("Recursive", b => b.Map(e => e.Id).Map(e => e.Child));
                else if (variant == "overlay")
                {
                    assembly.DeclareClass<DtoMetadataTests.Category>("Category", b => b.Map(e => e.Id));
                    cls = assembly.DeclareClass<DtoMetadataTests.Product>("Overlay", b => b.Map(e => e.Category)
                        .Map(e => e.ExternalCode, "Category.Extra").Map(e => e.ProductName, "Group.Name"));
                }
                else if (variant == "transformed")
                    cls = assembly.DeclareClass<DtoMetadataTests.Product>("Transformed", b => b
                        .MapConverted<int, string>(e => e.Id, "Id", value => value.ToString())
                        .ComputeInMemory("Name", e => e.ProductName));
                else
                    cls = assembly.DeclareClass<DtoMetadataTests.Product>("Ordinary", b => b.Map(e => e.Id));
                assembly.Build();
                var type = cls.GetBuiltType();
                snapshot = DtoShapeMetadata.GetForType(type);
                Assert.Same(cls.GetMetadata(), snapshot);
                Assert.NotEmpty(snapshot.Layout.Members);
                if (variant == "recursive") Assert.True(snapshot.ResolveField("Child.Child.Id").Success);
                if (variant == "overlay")
                {
                    Assert.True(snapshot.ResolveField("Category.Extra").Success);
                    Assert.False(DtoShapeMetadata.TryGetForType(type.GetProperty("Group").PropertyType, out _, out _));
                }
                return new WeakReference(type);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AssertRetainedThenRelease(WeakReference reference, ref DtoShapeMetadata snapshot)
        {
            Collect(reference);
            Assert.True(reference.IsAlive);
            Assert.NotEmpty(snapshot.Layout.Members);
            GC.KeepAlive(snapshot);
            snapshot = null;
        }

        private static void Collect(WeakReference reference)
        {
            for (var i = 0; i < 20 && reference.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }
}
