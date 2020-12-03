using System;
using System.Collections.Generic;
using Xunit;

namespace Kkts.Dynamic.UnitTest
{
    public class UnitTest1
    {
        private static Class DeclareClass()
        {
            var da = new DynamicAssembly();
            var bindings = new PropertyBinding[]
            {
                new PropertyBinding { EntityProperty = "Id", DtoProperty = "Id", Mode = BindingMode.TwoWay, IsPrimaryKey = true, PrimaryKeyOrder = 1 },
                new PropertyBinding { EntityProperty = "Name", DtoProperty = "Name", Mode = BindingMode.TwoWay, IsPrimaryKey = true, PrimaryKeyOrder = 0 },
                new PropertyBinding { EntityProperty = "DtoToEntity", DtoProperty = "DtoToEntity", Mode = BindingMode.OneWayToEntity },
                new PropertyBinding { EntityProperty = "EntityToDto", DtoProperty = "EntityToDto", Mode = BindingMode.OneWayToDto },
                new PropertyBinding { EntityProperty = "X", DtoProperty = "Fields.X", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Sample.Id", DtoProperty = "Sample.R.Id", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Sample.X", DtoProperty = "Fields.Y", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Sample.Description", DtoProperty = "Sample.Description", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Alternation", DtoProperty = "Alternation", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Alternation.PublishedDate", DtoProperty = "PublishedDate", Mode = BindingMode.OneWayToDto },
                new PropertyBinding { EntityProperty = "Array", DtoProperty = "Array", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "List", DtoProperty = "List", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Middle.Nested", DtoProperty = "Middle.Nested", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Middle.Nested.Array", DtoProperty = "Middle2.Array", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Middle.Nested.List", DtoProperty = "Middle3.List", Mode = BindingMode.TwoWay },
            };

            var nestedBindings = new PropertyBinding[]
            {
                new PropertyBinding { EntityProperty = "Id", DtoProperty = "Id", Mode = BindingMode.TwoWay, IsPrimaryKey = true, PrimaryKeyOrder = 1 },
                new PropertyBinding { EntityProperty = "Name", DtoProperty = "Name", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "X", DtoProperty = "Fields.X", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Sample.Id", DtoProperty = "Sample.R.Id", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Sample.X", DtoProperty = "Fields.Y", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Sample.Description", DtoProperty = "Sample.Description", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Alternation", DtoProperty = "Alternation", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Alternation.PublishedDate", DtoProperty = "PublishedDate", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Array", DtoProperty = "Array", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "List", DtoProperty = "List", Mode = BindingMode.TwoWay },
            };

            var bindings2 = new PropertyBinding[]
            {
                new PropertyBinding { EntityProperty = "Content", DtoProperty = "Content", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Id", DtoProperty = "Id", Mode = BindingMode.TwoWay, IsPrimaryKey = true, PrimaryKeyOrder = 1 },
            };

            var bindings3 = new PropertyBinding[]
            {
                new PropertyBinding { EntityProperty = "Token", DtoProperty = "Token", Mode = BindingMode.TwoWay },
                new PropertyBinding { EntityProperty = "Des", DtoProperty = "Des", Mode = BindingMode.TwoWay, IsPrimaryKey = true, PrimaryKeyOrder = 1 },
            };
            var cls1 = da.DeclareClass("Test", typeof(Source), bindings);
            var cls1_1 = da.DeclareClass("NestedSource", typeof(NestedSource), nestedBindings);
            var cls2 = da.DeclareClass("Test2", typeof(Source2), bindings2);
            var cls3 = da.DeclareClass("Test3", typeof(Element), bindings3);

            da.Build();

            var type1 = cls1.GetBuiltType();

            return cls1;
        }

        [Fact]
        public void InjectFrom_Success()
        {
            var cls1 = DeclareClass();
            var type1 = cls1.GetBuiltType();

            dynamic dto = Activator.CreateInstance(type1);
            var entity = new Source
            {
                Id = 10,
                Name = "test",
                DtoToEntity = "2020",
                X = 95,
                Sample = new NestedSouce { Id = 50, X = 86, Description = "Hello" },
                Alternation = new Source2 { Id = 46, Content = "Xin chao", PublishedDate = new DateTime(2030, 12, 22) },
                Array = new Element[] { new Element { Token = 1, Des = "1" }, new Element { Token = 2, Des = "2" } },
                List = new List<Element> { new Element { Token = 3, Des = "3" }, new Element { Token = 4, Des = "4" } }
            };
            entity.Middle = new Middle();
            entity.Middle.Nested = new NestedSource
            {
                Id = 11,
                Name = "test2",
                X = 99,
                Sample = new NestedSouce { Id = 50, X = 86, Description = "Hello" },
                Alternation = new Source2 { Id = 46, Content = "Xin chao", PublishedDate = new DateTime(2030, 12, 22) },
                Array = new Element[] { new Element { Token = 1, Des = "1" }, new Element { Token = 2, Des = "2" } },
                List = new List<Element> { new Element { Token = 3, Des = "3" }, new Element { Token = 4, Des = "4" } }
            };
            var entity2 = new Source
            {
                Id = 10,
                Name = "test",
                X = 95,
                Sample = null,
                Alternation = null,
                Array = null,
                List = null
            };
            Mapper.MapFromEntityToDto(dto, entity);
            // Test Mode OneWayToEntity
            Assert.True(dto.DtoToEntity == null);

            Assert.True(entity.Id == dto.Id);
            Assert.True(entity.Name == dto.Name);
            Assert.True(entity.X == dto.Fields.X);
            Assert.True(entity.Sample.Id == dto.Sample.R.Id);
            Assert.True(entity.Sample.X == dto.Fields.Y);
            Assert.True(entity.Alternation.Content == dto.Alternation.Content);
            Assert.True(entity.Alternation.Id == dto.Alternation.Id);
            Assert.True(entity.Alternation.PublishedDate == dto.PublishedDate);
            Assert.True(entity.Array[0].Token == dto.Array[0].Token);
            Assert.True(entity.Array[0].Des == dto.Array[0].Des);
            Assert.True(entity.Array[1].Token == dto.Array[1].Token);
            Assert.True(entity.Array[1].Des == dto.Array[1].Des);
            var index = 0;
            foreach(var item in dto.List)
            {
                Assert.True(entity.List[index].Token == item.Token);
                Assert.True(entity.List[index].Des == item.Des);
                ++index;
            }

            // nested prop
            Assert.True(entity.Middle.Nested.Id == dto.Middle.Nested.Id);
            Assert.True(entity.Middle.Nested.Name == dto.Middle.Nested.Name);
            Assert.True(entity.Middle.Nested.X == dto.Middle.Nested.Fields.X);
            Assert.True(entity.Middle.Nested.Sample.Id == dto.Middle.Nested.Sample.R.Id);
            Assert.True(entity.Middle.Nested.Sample.X == dto.Middle.Nested.Fields.Y);
            Assert.True(entity.Middle.Nested.Alternation.Content == dto.Middle.Nested.Alternation.Content);
            Assert.True(entity.Middle.Nested.Alternation.Id == dto.Middle.Nested.Alternation.Id);
            Assert.True(entity.Middle.Nested.Alternation.PublishedDate == dto.Middle.Nested.PublishedDate);
            Assert.True(entity.Middle.Nested.Array[0].Token == dto.Middle.Nested.Array[0].Token);
            Assert.True(entity.Middle.Nested.Array[0].Des == dto.Middle.Nested.Array[0].Des);
            Assert.True(entity.Middle.Nested.Array[1].Token == dto.Middle.Nested.Array[1].Token);
            Assert.True(entity.Middle.Nested.Array[1].Des == dto.Middle.Nested.Array[1].Des);
            index = 0;
            foreach (var item in dto.Middle.Nested.List)
            {
                Assert.True(entity.Middle.Nested.List[index].Token == item.Token);
                Assert.True(entity.Middle.Nested.List[index].Des == item.Des);
                ++index;
            }

            Assert.True(entity.Middle.Nested.Array[0].Token == dto.Middle2.Array[0].Token);
            Assert.True(entity.Middle.Nested.Array[0].Des == dto.Middle2.Array[0].Des);
            Assert.True(entity.Middle.Nested.Array[1].Token == dto.Middle2.Array[1].Token);
            Assert.True(entity.Middle.Nested.Array[1].Des == dto.Middle2.Array[1].Des);
            index = 0;
            foreach (var item in dto.Middle3.List)
            {
                Assert.True(entity.Middle.Nested.List[index].Token == item.Token);
                Assert.True(entity.Middle.Nested.List[index].Des == item.Des);
                ++index;
            }
            var keys = DtoObject.GetIds(dto);
            Assert.True(entity.Name == keys[0]);
            Assert.True(entity.Id == keys[1]);
            dynamic dto2 = Activator.CreateInstance(type1);
            Mapper.MapFromEntityToDto(dto2, entity2);
            Assert.Null(entity2.Sample);
            Assert.Null(dto2.Sample);
            Assert.Null(entity2.Alternation);
            Assert.Null(dto2.Alternation);
            Assert.Null(dto2.List);
            Assert.Null(dto2.Array);
            //var entity2 = new Source { Alternation = new Source2 { PublishedDate = new DateTime(2021, 12, 20) } };
            var dtoObj = dto as IDtoObject;
            Assert.NotNull(dtoObj);
            var exp = cls1.BuildSelectorExpression();
            Assert.NotNull(exp);
        }

        [Fact]
        public void InjectTo_Success()
        {
            var cls1 = DeclareClass();
            var type1 = cls1.GetBuiltType();
            dynamic dto = Activator.CreateInstance(type1);
            var tmp = new Source
            {
                Id = 10,
                Name = "test",
                EntityToDto = "2020",
                X = 95,
                Sample = new NestedSouce { Id = 50, X = 86, Description = "Hello" },
                Alternation = new Source2 { Id = 46, Content = "Xin chao", PublishedDate = new DateTime(2030, 12, 22) },
                Array = new Element[] { new Element { Token = 1, Des = "1" }, new Element { Token = 2, Des = "2" } },
                List = new List<Element> { new Element { Token = 3, Des = "3" }, new Element { Token = 4, Des = "4" } }
            };
            tmp.Middle = new Middle();
            tmp.Middle.Nested = new NestedSource
            {
                Id = 11,
                Name = "test2",
                X = 99,
                Sample = new NestedSouce { Id = 50, X = 86, Description = "Hello" },
                Alternation = new Source2 { Id = 46, Content = "Xin chao", PublishedDate = new DateTime(2030, 12, 22) },
                Array = new Element[] { new Element { Token = 1, Des = "1" }, new Element { Token = 2, Des = "2" } },
                List = new List<Element> { new Element { Token = 3, Des = "3" }, new Element { Token = 4, Des = "4" } }
            };
            var date = new DateTime(2020, 12, 22);
            var entity2 = new Source
            {
                Id = 10,
                Name = "test",
                X = 95,
                Sample = null,
                Alternation = new Source2
                {
                    PublishedDate = date
                },
                Array = null,
                List = null
            };
            Mapper.MapFromEntityToDto(dto, tmp);
            dto.EntityToDto = "2020";
            // End preparing data;

            var entity = new Source();
            Mapper.MapFromDtoToEntity(dto, entity);
            // Test Mode OneWayToDto
            Assert.Null(entity.EntityToDto);

            Assert.True(entity.Id == dto.Id);
            Assert.True(entity.Name == dto.Name);
            Assert.True(entity.X == dto.Fields.X);
            Assert.True(entity.Sample.Id == dto.Sample.R.Id);
            Assert.True(entity.Sample.X == dto.Fields.Y);
            Assert.True(entity.Alternation.Content == dto.Alternation.Content);
            Assert.True(entity.Alternation.Id == dto.Alternation.Id);
            Assert.True(entity.Array[0].Token == dto.Array[0].Token);
            Assert.True(entity.Array[0].Des == dto.Array[0].Des);
            Assert.True(entity.Array[1].Token == dto.Array[1].Token);
            Assert.True(entity.Array[1].Des == dto.Array[1].Des);
            var index = 0;
            foreach (var item in dto.List)
            {
                Assert.True(entity.List[index].Token == item.Token);
                Assert.True(entity.List[index].Des == item.Des);
                ++index;
            }

            // nested prop
            Assert.True(entity.Middle.Nested.Id == dto.Middle.Nested.Id);
            Assert.True(entity.Middle.Nested.Name == dto.Middle.Nested.Name);
            Assert.True(entity.Middle.Nested.X == dto.Middle.Nested.Fields.X);
            Assert.True(entity.Middle.Nested.Sample.Id == dto.Middle.Nested.Sample.R.Id);
            Assert.True(entity.Middle.Nested.Sample.X == dto.Middle.Nested.Fields.Y);
            Assert.True(entity.Middle.Nested.Alternation.Content == dto.Middle.Nested.Alternation.Content);
            Assert.True(entity.Middle.Nested.Alternation.Id == dto.Middle.Nested.Alternation.Id);
            Assert.True(entity.Middle.Nested.Alternation.PublishedDate == dto.Middle.Nested.PublishedDate);
            Assert.True(entity.Middle.Nested.Array[0].Token == dto.Middle.Nested.Array[0].Token);
            Assert.True(entity.Middle.Nested.Array[0].Des == dto.Middle.Nested.Array[0].Des);
            Assert.True(entity.Middle.Nested.Array[1].Token == dto.Middle.Nested.Array[1].Token);
            Assert.True(entity.Middle.Nested.Array[1].Des == dto.Middle.Nested.Array[1].Des);
            index = 0;
            foreach (var item in dto.Middle.Nested.List)
            {
                Assert.True(entity.Middle.Nested.List[index].Token == item.Token);
                Assert.True(entity.Middle.Nested.List[index].Des == item.Des);
                ++index;
            }

            Assert.True(entity.Middle.Nested.Array[0].Token == dto.Middle2.Array[0].Token);
            Assert.True(entity.Middle.Nested.Array[0].Des == dto.Middle2.Array[0].Des);
            Assert.True(entity.Middle.Nested.Array[1].Token == dto.Middle2.Array[1].Token);
            Assert.True(entity.Middle.Nested.Array[1].Des == dto.Middle2.Array[1].Des);
            index = 0;
            foreach (var item in dto.Middle3.List)
            {
                Assert.True(entity.Middle.Nested.List[index].Token == item.Token);
                Assert.True(entity.Middle.Nested.List[index].Des == item.Des);
                ++index;
            }
            var keys = DtoObject.GetIds(dto);
            Assert.True(entity.Name == keys[0]);
            Assert.True(entity.Id == keys[1]);
            Mapper.MapFromDtoToEntity(dto, entity2);
            Assert.NotNull(entity2.Sample);
            Assert.NotNull(entity2.Sample);
            Assert.NotNull(entity2.Alternation);
            Assert.True(entity2.Alternation.PublishedDate == date);
            Assert.NotNull(entity2.List);
            Assert.NotNull(entity2.Array);
            var dtoObj = dto as IDtoObject;
            Assert.NotNull(dtoObj);
            var exp = cls1.BuildSelectorExpression();
            Assert.NotNull(exp);
        }
    }

    public class Source
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public int X;

        public NestedSouce Sample { get; set; }

        public Source2 Alternation { get; set; }

        public Element[] Array { get; set; }

        public IList<Element> List { get; set; }

        public Middle Middle { get; set; }

        public string DtoToEntity { get; set; }

        public string EntityToDto { get; set; }
    }

    public class Middle
    {
        public NestedSource Nested { get; set; }
    }

    public class NestedSource
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public int X;

        public NestedSouce Sample { get; set; }

        public Source2 Alternation { get; set; }

        public Element[] Array { get; set; }

        public IList<Element> List { get; set; }
    }

    public class Element
    {
        public int Token { get; set; }

        public string Des { get; set; }
    }

    public class Source2
    {
        public int Id { get; set; }

        public string Content { get; set; }

        public DateTime PublishedDate { get; set; }
    }

    public class NestedSouce
    {
        public int Id { get; set; }

        public string Description { get; set; }

        public int X;
    }
}
