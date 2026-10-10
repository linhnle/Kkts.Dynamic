using System;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class TransformationTests
    {
        public enum State { Draft, Published }
        public class Entity
        {
            [DisplayName("Identifier")]
            public int Id { get; set; }
            public State Status { get; set; }
            public string First { get; set; }
            public string Last { get; set; }
            public Entity Child { get; set; }
        }
        private static object Get(object dto, string name) => dto.GetType().GetProperty(name).GetValue(dto);
        private static void Set(object dto, string name, object value) => dto.GetType().GetProperty(name).SetValue(dto, value);

        [Fact]
        public void ExpressionsProduceConvertedKeysComputedValuesAndBidirectionalParity()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Dto", b => b
                    .MapConverted<int, string>(e => e.Id, "Id", v => v.ToString(), v => int.Parse(v),
                        BindingMode.TwoWay, isPrimaryKey: true)
                    .MapConverted<State, string>(e => e.Status, "Status", v => v.ToString(),
                        v => (State)Enum.Parse(typeof(State), v), BindingMode.TwoWay)
                    .Compute("FullName", e => e.First + " " + e.Last)
                    .Compute("Fields.Label", e => "ID: " + e.Id));
                assembly.Build();
                var source = new Entity { Id = 4, Status = State.Published, First = "Ada", Last = "Lovelace" };
                foreach (var dto in new[] { cls.CreateDto(source), cls.BuildSelectorExpression().Compile().DynamicInvoke(source), Project(cls, source) })
                {
                    Assert.Equal("4", Get(dto, "Id"));
                    Assert.Equal("Published", Get(dto, "Status"));
                    Assert.Equal("Ada Lovelace", Get(dto, "FullName"));
                    Assert.Equal("ID: 4", Get(Get(dto, "Fields"), "Label"));
                    Assert.Equal(new object[] { "4" }, dto.GetIds());
                    Set(dto, "Id", "9");
                    Set(dto, "Status", "Draft");
                    var target = new Entity { First = "preserved" };
                    Mapper.MapFromDtoToEntity(dto, target);
                    Assert.Equal(9, target.Id);
                    Assert.Equal(State.Draft, target.Status);
                    Assert.Equal("preserved", target.First);
                }
                var visitor = new DelegateInvocationVisitor();
                visitor.Visit(cls.BuildSelectorExpression());
                Assert.False(visitor.Found);
            }
        }

        private static object Project(Class cls, Entity entity)
        {
            var source = new[] { entity }.AsQueryable();
            var call = Expression.Call(typeof(Queryable), nameof(Queryable.Select),
                new[] { typeof(Entity), cls.GetBuiltType() }, source.Expression, Expression.Quote(cls.BuildSelectorExpression()));
            return source.Provider.CreateQuery(call).Cast<object>().Single();
        }

        private sealed class DelegateInvocationVisitor : ExpressionVisitor
        {
            internal bool Found;
            protected override Expression VisitInvocation(InvocationExpression node) { Found = true; return base.VisitInvocation(node); }
            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is Delegate) Found = true;
                return base.VisitConstant(node);
            }
        }

        [Fact]
        public void DelegateOnlyTransformsWarnAndRejectSelectorsButReverseOnlyDelegateDoesNot()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Dto", b => b.ComputeInMemory("Name", e => e.First));
                var report = assembly.Validate();
                Assert.True(report.IsValid);
                Assert.Contains(report.Diagnostics, d => d.Code == "ProjectionUnavailable" && d.Severity == MappingDiagnosticSeverity.Warning);
                assembly.Build();
                Assert.Equal("value", Get(cls.CreateDto(new Entity { First = "value" }), "Name"));
                Assert.Contains("Name", Assert.Throws<InvalidOperationException>(() => cls.BuildSelectorExpression()).Message);
                Assert.Throws<InvalidOperationException>(() => cls.BuildSelectorExpression());
            }
            using (var assembly = new DynamicAssembly())
            {
                Expression<Func<int, string>> forward = v => v.ToString();
                Func<string, int> reverse = int.Parse;
                var cls = assembly.DeclareClass<Entity>("Dto", b => b.Map(new PropertyBinding
                {
                    EntityProperty = "Id", DtoValueType = typeof(string), ToDtoExpression = forward,
                    ToEntityInMemory = reverse, Mode = BindingMode.TwoWay
                }));
                assembly.Build();
                var dto = cls.BuildSelectorExpression().Compile().DynamicInvoke(new Entity { Id = 3 });
                var target = new Entity();
                Mapper.MapFromDtoToEntity(dto, target);
                Assert.Equal(3, target.Id);
            }
        }

        public class Parent { public Entity Child { get; set; } }

        [Fact]
        public void DelegateOnlyNestedShapeFailsWithPath()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Parent>("Parent", b => b.Map(p => p.Child));
                assembly.DeclareClass<Entity>("Child", b => b.ComputeInMemory("Name", e => e.First));
                assembly.Build();
                var error = Assert.Throws<InvalidOperationException>(() => cls.BuildSelectorExpression());
                Assert.Contains("Child.Name", error.Message);
                Assert.Contains("Child", error.Message);
            }
        }

        [Fact]
        public void ComputedNestedOverlaysCreateOnlySyntheticGroupsAndMatchProjection()
        {
            using (var assembly = new DynamicAssembly())
            {
                assembly.DeclareClass<Entity>("Child", b => b.Map(e => e.Id));
                var cls = assembly.DeclareClass<Parent>("Parent", b => b.Map(p => p.Child)
                    .Compute("Child.Fields.Name", p => p.Child.First + "!"));
                assembly.Build();
                var source = new Parent { Child = new Entity { Id = 8, First = "name" } };
                foreach (var dto in new[] { cls.CreateDto(source), cls.BuildSelectorExpression().Compile().DynamicInvoke(source) })
                {
                    var child = Get(dto, "Child");
                    Assert.Equal(8, Get(child, "Id"));
                    Assert.Equal("name!", Get(Get(child, "Fields"), "Name"));
                }
            }
        }

        [Fact]
        public void IntermediateNullSkipsConversionAndDirectNullReachesIt()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Dto", b => b
                    .MapConverted<string, string>(e => e.Child.First, "Name", value => value ?? "null"));
                assembly.Build();
                var dto = cls.CreateDto(new Entity { Child = new Entity() });
                Assert.Equal("null", Get(dto, "Name"));
                Mapper.MapFromEntityToDto(dto, new Entity());
                Assert.Equal("null", Get(dto, "Name"));
                Assert.Null(Get(cls.BuildSelectorExpression().Compile().DynamicInvoke(new Entity()), "Name"));
            }
        }

        [Fact]
        public void BadTransformationConfigurationsReportAllIndependentErrors()
        {
            using (var assembly = new DynamicAssembly())
            {
                var report = assembly.ValidateClass("Dto", typeof(Entity), new[]
                {
                    new PropertyBinding { EntityProperty = "Id", DtoProperty = "MissingReverse", DtoValueType = typeof(string),
                        ToDtoExpression = (Expression<Func<int, string>>)(v => v.ToString()), Mode = BindingMode.TwoWay },
                    new PropertyBinding { EntityProperty = "Status", DtoValueType = typeof(string),
                        ToDtoExpression = (Expression<Func<long, string>>)(v => v.ToString()), Mode = BindingMode.OneWayToDto },
                    new PropertyBinding { DtoProperty = "Computed", ComputedExpression = (Expression<Func<Entity, int>>)(e => e.Id),
                        Mode = BindingMode.TwoWay, IsPrimaryKey = true },
                    new PropertyBinding { EntityProperty = "First", DtoProperty = "Conflict", DtoValueType = typeof(string),
                        NestedShape = "Anything", ToDtoExpression = (Expression<Func<string, string>>)(v => v),
                        ToDtoInMemory = (Func<string, string>)(v => v), Mode = BindingMode.OneWayToDto }
                });
                foreach (var code in new[] { "MissingTransformation", "TransformationSignature", "ComputedBinding", "BindingKind", "TransformationConflict" })
                    Assert.Contains(report.Diagnostics, d => d.Code == code);
                Assert.Empty(assembly.Classes);
            }
        }

        [Fact]
        public void ConvertedAndComputedAttributesAreSafe()
        {
            using (var assembly = new DynamicAssembly())
            {
                var ordinary = assembly.DeclareClass<Entity>("Ordinary", b => b.Map(e => e.Id));
                var same = assembly.DeclareClass<Entity>("Same", b => b.MapConverted<int, int>(e => e.Id, "Id", v => v, v => v, BindingMode.TwoWay));
                var different = assembly.DeclareClass<Entity>("Different", b => b.MapConverted<int, string>(e => e.Id, "Id", v => v.ToString(), v => int.Parse(v), BindingMode.TwoWay));
                var computed = assembly.DeclareClass<Entity>("Computed", b => b.Compute("Id", e => e.Id));
                assembly.Build();
                foreach (var cls in new[] { ordinary, same })
                    Assert.NotNull(cls.GetBuiltType().GetProperty("Id").GetCustomAttributes(typeof(DisplayNameAttribute), false).SingleOrDefault());
                foreach (var cls in new[] { different, computed })
                    Assert.Empty(cls.GetBuiltType().GetProperty("Id").GetCustomAttributes(typeof(DisplayNameAttribute), false));
            }
        }

        [Fact]
        public void ConverterExceptionsAreDirectAndMappingSurvivesDisposal()
        {
            var assembly = new DynamicAssembly();
            var cls = assembly.DeclareClass<Entity>("Dto", b => b
                .MapConvertedInMemory<int, string>(e => e.Id, "Id", v => v.ToString(), v => int.Parse(v), BindingMode.TwoWay));
            assembly.Build();
            var dto = cls.CreateDto(new Entity { Id = 5 });
            ((IDisposable)assembly).Dispose();
            Set(dto, "Id", "invalid");
            Assert.Throws<FormatException>(() => Mapper.MapFromDtoToEntity(dto, new Entity()));
            Mapper.MapFromEntityToDto(dto, new Entity { Id = 6 });
            Assert.Equal("6", Get(dto, "Id"));
        }

        [Fact]
        public void TransformationDelegatesSupportConcurrentDistinctObjectsAndCollectibility()
        {
            var reference = ExerciseCollectibleTransforms();
            for (var i = 0; i < 20 && reference.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(reference.IsAlive);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ExerciseCollectibleTransforms()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Dto", b => b
                    .MapConverted<int, string>(e => e.Id, "Id", v => v.ToString(), v => int.Parse(v), BindingMode.TwoWay)
                    .Compute("Name", e => e.First));
                assembly.Build();
                var type = cls.GetBuiltType();
                Parallel.For(0, 50, i =>
                {
                    var dto = cls.CreateDto(new Entity { Id = i, First = "name" });
                    var target = new Entity();
                    Mapper.MapFromDtoToEntity(dto, target);
                    Assert.Equal(i, target.Id);
                });
                cls.BuildSelectorExpression().Compile();
                return new WeakReference(type);
            }
        }
    }
}
