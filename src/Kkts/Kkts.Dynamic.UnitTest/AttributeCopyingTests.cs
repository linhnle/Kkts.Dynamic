using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class AttributeCopyingTests
    {
        public enum Choice : long { First = 1, Large = 4294967296 }

        [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
        public class OptionsAttribute : Attribute
        {
            public OptionsAttribute(string[] values) { Values = values; }
            public string[] Values { get; }
        }

        [AttributeUsage(AttributeTargets.Property)]
        public class ValuesAttribute : Attribute
        {
            public ValuesAttribute(int number, string text, Choice choice, Type type,
                int[] numbers, Choice[] choices, Type[] types, object boxed, object[] objects)
            {
                Number = number; Text = text; Choice = choice; Type = type;
                Numbers = numbers; Choices = choices; Types = types; Boxed = boxed; Objects = objects;
            }
            public int Number { get; }
            public string Text { get; }
            public Choice Choice { get; }
            public Type Type { get; }
            public int[] Numbers { get; }
            public Choice[] Choices { get; }
            public Type[] Types { get; }
            public object Boxed { get; }
            public object[] Objects { get; }
            public string[] Names { get; set; }
            public string[] NameField;
            public Choice NamedChoice { get; set; }
            public Choice ChoiceField;
            public Type NamedType { get; set; }
            public Type TypeField;
            public object NamedObject { get; set; }
            public object ObjectField;
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
        public class BoxedAttribute : Attribute
        {
            public BoxedAttribute(object value) { Value = value; }
            public object Value { get; }
            public object Property { get; set; }
            public object Field;
        }

        public class Entity
        {
            [Options(new[] { "a", "b" })]
            public int Id { get; set; }

            [Options(new string[] { "a", null, "b" })]
            public int Strings { get; set; }

            [Options(null)]
            public int NullStrings { get; set; }

            [Options(new string[0])]
            public int EmptyStrings { get; set; }

            [Options(new[] { "field" })]
            public int Field;

            [Options(new[] { "child" })]
            public Child Child { get; set; }

            [Options(new[] { "children" })]
            public Child[] Children { get; set; }

            [Values(7, "text", Choice.Large, typeof(string),
                new[] { 3, 1 }, new[] { Choice.Large, Choice.First }, new[] { typeof(int), null },
                Choice.Large, new object[] { (byte)1, (sbyte)-1, (short)-2, (ushort)2, 3, 4U,
                    -5L, 6UL, 1.5F, 2.5, 'x', true, "text", Choice.Large, typeof(Entity), null, new[] { 9, 8 } },
                Names = new[] { "property", "p" }, NameField = new[] { "field", "f" },
                NamedChoice = Choice.Large, ChoiceField = Choice.First,
                NamedType = typeof(Entity), TypeField = typeof(Child),
                NamedObject = new[] { Choice.Large, Choice.First },
                ObjectField = new object[] { Choice.First, typeof(string), null })]
            public int NonEmpty { get; set; }

            [Values(0, null, Choice.First, null, null, null, null, null, null,
                Names = null, NameField = null, NamedObject = null, ObjectField = null,
                NamedType = null, TypeField = null)]
            public int Nulls { get; set; }

            [Values(1, "", Choice.First, typeof(int), new int[0], new Choice[0], new Type[0],
                new string[0], new object[0], Names = new string[0], NameField = new string[0],
                NamedObject = new int[0], ObjectField = new Type[0])]
            public int Empty { get; set; }

            [Boxed(7, Property = Choice.Large, Field = typeof(string))]
            [Boxed(Choice.Large, Property = new[] { "a", null }, Field = new[] { Choice.First })]
            [Boxed(typeof(Entity), Property = 2.5, Field = 'c')]
            [Boxed(new[] { 3, 1 }, Property = null, Field = null)]
            [Boxed(null, Property = new object[] { 1, Choice.Large, typeof(int), null }, Field = true)]
            public int Boxed { get; set; }
        }

        public class Child { public int Id { get; set; } }

        private static void AssertValue(object expected, object actual)
        {
            if (expected == null)
            {
                Assert.Null(actual);
                return;
            }
            Assert.NotNull(actual);
            if (expected is Type expectedType)
            {
                Assert.Equal(expectedType, Assert.IsAssignableFrom<Type>(actual));
                return;
            }
            Assert.Equal(expected.GetType(), actual.GetType());
            if (expected is Array array)
            {
                var copy = Assert.IsAssignableFrom<Array>(actual);
                Assert.Equal(array.Length, copy.Length);
                for (var i = 0; i < array.Length; i++)
                    AssertValue(array.GetValue(i), copy.GetValue(i));
            }
            else
            {
                Assert.Equal(expected, actual);
            }
        }

        private static void AssertAttributes(MemberInfo source, PropertyInfo destination)
        {
            var expected = source.GetCustomAttributes(false).OfType<Attribute>()
                .Where(a => a.GetType().DeclaringType == typeof(AttributeCopyingTests)).ToArray();
            var actual = destination.GetCustomAttributes(false).OfType<Attribute>()
                .Where(a => a.GetType().DeclaringType == typeof(AttributeCopyingTests)).ToArray();
            Assert.NotEmpty(expected);
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i].GetType(), actual[i].GetType());
                foreach (var property in expected[i].GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    AssertValue(property.GetValue(expected[i]), property.GetValue(actual[i]));
                foreach (var field in expected[i].GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    AssertValue(field.GetValue(expected[i]), field.GetValue(actual[i]));
            }
        }

        [Fact]
        public void StringArrayAttributeBuildsAndRetainsContents()
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Dto", b => b.Map(e => e.Id));
                Assert.True(assembly.Validate().IsValid);
                assembly.Build();
                var attribute = Assert.Single(cls.GetBuiltType().GetProperty("Id").GetCustomAttributes<OptionsAttribute>());
                Assert.Equal(new[] { "a", "b" }, Assert.IsType<string[]>(attribute.Values));
            }
        }

        [Theory]
        [InlineData(nameof(Entity.Strings))]
        [InlineData(nameof(Entity.NullStrings))]
        [InlineData(nameof(Entity.EmptyStrings))]
        [InlineData(nameof(Entity.NonEmpty))]
        [InlineData(nameof(Entity.Nulls))]
        [InlineData(nameof(Entity.Empty))]
        [InlineData(nameof(Entity.Boxed))]
        [InlineData(nameof(Entity.Field))]
        public void ConstructorAndNamedArgumentsRetainExactTypesAndValues(string name)
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass("Dto", typeof(Entity), new[]
                {
                    new PropertyBinding { EntityProperty = name, Mode = BindingMode.TwoWay }
                });
                Assert.True(assembly.Validate().IsValid);
                assembly.Build();
                AssertAttributes(typeof(Entity).GetMember(name).Single(), cls.GetBuiltType().GetProperty(name));
            }
        }

        [Theory]
        [InlineData(BindingMode.TwoWay, true)]
        [InlineData(BindingMode.OneWayToEntity, true)]
        [InlineData(BindingMode.OneWayToDto, false)]
        public void BindingModesRetainAttributeEligibility(BindingMode mode, bool copies)
        {
            using (var assembly = new DynamicAssembly())
            {
                var cls = assembly.DeclareClass<Entity>("Dto", b => b.Map(e => e.Id, mode: mode));
                assembly.Build();
                var attributes = cls.GetBuiltType().GetProperty("Id").GetCustomAttributes<OptionsAttribute>().ToArray();
                if (copies)
                    Assert.Equal(new[] { "a", "b" }, Assert.Single(attributes).Values);
                else
                    Assert.Empty(attributes);
            }
        }

        [Fact]
        public void TransformationsComputationsAndTypeSubstitutionRetainAttributePolicy()
        {
            using (var assembly = new DynamicAssembly())
            {
                var same = assembly.DeclareClass<Entity>("Same", b =>
                    b.MapConverted<int, int>(e => e.Id, "Id", v => v, v => v, BindingMode.TwoWay));
                var changed = assembly.DeclareClass<Entity>("Changed", b =>
                    b.MapConverted<int, string>(e => e.Id, "Id", v => v.ToString(), v => int.Parse(v), BindingMode.TwoWay));
                var computed = assembly.DeclareClass<Entity>("Computed", b => b.Compute("Id", e => e.Id));
                var substituted = assembly.DeclareClass<Entity>("Substituted", b => b.Map(e => e.Child).Map(e => e.Children));
                assembly.DeclareClass<Child>("ChildDto", b => b.Map(e => e.Id));
                assembly.Build();
                Assert.Equal(new[] { "a", "b" },
                    Assert.Single(same.GetBuiltType().GetProperty("Id").GetCustomAttributes<OptionsAttribute>()).Values);
                Assert.Empty(changed.GetBuiltType().GetProperty("Id").GetCustomAttributes<OptionsAttribute>());
                Assert.Empty(computed.GetBuiltType().GetProperty("Id").GetCustomAttributes<OptionsAttribute>());
                Assert.Empty(substituted.GetBuiltType().GetProperty("Child").GetCustomAttributes<OptionsAttribute>());
                Assert.Empty(substituted.GetBuiltType().GetProperty("Children").GetCustomAttributes<OptionsAttribute>());
            }
        }
    }
}
