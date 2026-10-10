using System;
using System.Linq;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class MappingValidationTests
    {
        public class Entity
        {
            public int Id { get; set; }
            public int ReadOnly => 1;
            public Child Nested { get; set; }
            public Entity Self { get; set; }
        }
        public class Child
        {
            public Child(int id) { Id = id; }
            public int Id { get; set; }
        }

        [Fact]
        public void RawValidationAggregatesIndependentErrorsWithoutRegistrationOrMutation()
        {
            using (var assembly = new DynamicAssembly())
            {
                var bindings = new[]
                {
                    new PropertyBinding { EntityProperty = ".Id", DtoProperty = "Bad" },
                    new PropertyBinding { EntityProperty = "Id", DtoProperty = "Value" },
                    new PropertyBinding { EntityProperty = "Missing", DtoProperty = "value" },
                    new PropertyBinding { EntityProperty = "ReadOnly" },
                    new PropertyBinding { EntityProperty = "Nested.Id", DtoProperty = "ChildId" },
                    new PropertyBinding { EntityProperty = "Id", DtoProperty = "Keys.Id", IsPrimaryKey = true }
                };
                var report = assembly.ValidateClass("Candidate", typeof(Entity), bindings);
                Assert.False(report.IsValid);
                foreach (var code in new[] { "EntityPath", "DuplicateDtoPath", "MemberPath", "Setter", "Constructor", "PrimaryKeyPath", "DuplicateEntityWrite" })
                    Assert.Contains(report.Diagnostics, d => d.Code == code);
                var setter = Assert.Single(report.Diagnostics, d => d.Code == "Setter");
                Assert.Equal("Candidate", setter.ClassName);
                Assert.Equal(3, setter.BindingIndex);
                Assert.Equal("ReadOnly", setter.EntityPath);
                Assert.Equal("ReadOnly", setter.DtoPath);
                Assert.Equal("DtoToEntity", setter.Direction);
                Assert.NotEmpty(setter.SuggestedFix);
                Assert.Null(bindings[3].DtoProperty);
                Assert.Empty(assembly.Classes);
                Assert.Equal(report.Diagnostics.Select(d => (d.Code, d.BindingIndex)),
                    assembly.ValidateClass("Candidate", typeof(Entity), bindings).Diagnostics.Select(d => (d.Code, d.BindingIndex)));
                var cls = assembly.DeclareClass<Entity>("Valid", b => b.Map(p => p.Id));
                Assert.True(assembly.Validate().IsValid);
                Assert.Throws<InvalidOperationException>(() => cls.GetBuiltType());
                assembly.Build();
                Assert.Equal(5, cls.GetBuiltType().GetProperty("Id").GetValue(cls.CreateDto(new Entity { Id = 5 })));
                Assert.True(assembly.Validate().IsValid);
            }
        }

        [Fact]
        public void BuildReturnsReportAndRetainsFaultLifecycle()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass("Invalid", typeof(Entity), new[]
                {
                    new PropertyBinding { EntityProperty = "Missing", DtoProperty = "Missing" },
                    new PropertyBinding { EntityProperty = "ReadOnly" }
                });
                var report = assembly.Validate();
                Assert.Equal(2, report.Diagnostics.Count);
                var error = Assert.Throws<MappingValidationException>(() => assembly.Build());
                Assert.Equal(2, error.Report.Diagnostics.Count);
                Assert.Same(error, Assert.Throws<InvalidOperationException>(() => assembly.Build()).InnerException);
                Assert.Throws<InvalidOperationException>(() => cls.GetBuiltType());
            }
        }

        [Fact]
        public void CandidateNameAndClassInputsHaveActionableDiagnostics()
        {
            using (var assembly = new DynamicAssembly())
            {
                Assert.Equal(3, assembly.ValidateClass(null, null, null).Diagnostics.Count);
                assembly.DeclareClass<Entity>("Dto", b => b.Map(p => p.Id));
                Assert.Contains(assembly.ValidateClass("Dto", typeof(Entity), new[]
                {
                    new PropertyBinding { EntityProperty = "ReadOnly", Mode = BindingMode.OneWayToDto }
                }).Diagnostics, d => d.Code == "ClassConflict");
                Assert.True(assembly.ValidateClass("Dto", typeof(Entity), new[]
                {
                    new PropertyBinding { EntityProperty = "Id" }
                }).IsValid);
                Assert.Throws<ArgumentException>(() => assembly.DeclareClass("Bad", typeof(Entity),
                    new[] { new PropertyBinding { EntityProperty = ".Id" } }));
                Assert.Single(assembly.Classes);
            }

        }

        [Fact]
        public void RawCandidateParticipatesInNestedResolutionWithoutEmission()
        {
            using (var assembly = new DynamicAssembly())
            {
                var candidate = new[]
                {
                    new PropertyBinding { EntityProperty = "Self", NestedShape = "Candidate" }
                };
                Assert.True(assembly.ValidateClass("Candidate", typeof(Entity), candidate).IsValid);
                assembly.DeclareClass<Entity>("Other", b => b.Map(p => p.Id));
                candidate[0].NestedShape = null;
                Assert.Contains(assembly.ValidateClass("Candidate", typeof(Entity), candidate).Diagnostics,
                    d => d.Code == "AlternativeType" && d.Message.Contains("multiple shapes"));
                Assert.Single(assembly.Classes);
            }
        }

        [Fact]
        public void NullBindingDiagnosticRetainsItsInputLocation()
        {
            using (var assembly = new DynamicAssembly())
            {
                var report = assembly.ValidateClass("Candidate", typeof(Entity), new[]
                {
                    new PropertyBinding { EntityProperty = "Id" },
                    null
                });
                var diagnostic = Assert.Single(report.Diagnostics, d => d.Code == "NullBinding");
                Assert.Equal(1, diagnostic.BindingIndex);
                Assert.Null(diagnostic.EntityPath);
                Assert.Null(diagnostic.DtoPath);
                Assert.False(report.IsValid);
            }
        }
    }
}
