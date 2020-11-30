# Kkts.Dynamic
A lightweight and dynamic mapper, you don't need to declare any classes for View Model or Data Transfer Object, just declare property bindings.


get via nuget **[Kkts.Dynamic](https://www.nuget.org/packages/Kkts.Dynamic)** 

### Sample class
``` csharp
public class Product
{
    public int Id { get; set; }

    public string Code { get; set; }

    public string ProductName { get; set; }

    public double Price { get; set; }

    public string ExternalCode { get; set; }

    public string ExternalInformation { get; set; }

    public string Sku { get; set; }

    public Category Category { get; set; }

    public List<Tag> Tags { get; set; }
}

public class Category
{
    public int Id { get; set; }

    public string Name { get; set; }
}

public class Tag
{
    public int Id { get; set; }

    public string Name { get; set; }
}
```
#### Sample Json Data of product
``` javascript
{
	id: 1,
	code: 'P01',
	productName: 'Test Product',
	price: 20.5,
	externalCode: 'P01PHONE',
	externalInformation: 'Product information',
	sku: 'VN_HCM',
	category: {
		id: 1220,
		name: 'Mobile'
	},
	tags: [
		{
			id: 1,
			name: 'phone'
		},
		{
			id: 2,
			name: 'mobile'
		},
		{
			id: 3,
			name: 'mobile_phone'
		}
	]
}
```

### Usage (Map product to dto)
``` csharp
var assembly = new DynamicAssembly();
// Bindings
var productBindings = new PropertyBinding[]
{
    new PropertyBinding { EntityProperty = "Id", DtoProperty = "Id", Mode = BindingMode.OneWayToDto, IsPrimaryKey = true, PrimaryKeyOrder = 0 },
    new PropertyBinding { EntityProperty = "ProductName", DtoProperty = "Name", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "Code", DtoProperty = "Code", Mode = BindingMode.TwoWay, IsPrimaryKey = true, PrimaryKeyOrder = 1 },
    new PropertyBinding { EntityProperty = "Price", DtoProperty = "Price", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "ExternalCode", DtoProperty = "External.Code", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "ExternalInformation", DtoProperty = "External.Information", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "Sku", DtoProperty = "Sku", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "Category", DtoProperty = "Category", Mode = BindingMode.TwoWay }, // map category
    new PropertyBinding { EntityProperty = "Category.Name", DtoProperty = "CategoryName", Mode = BindingMode.TwoWay }, // map Category.Name as CategoryName
    new PropertyBinding { EntityProperty = "Tags", DtoProperty = "Tags", Mode = BindingMode.TwoWay }
};

var categoryBindings = new PropertyBinding[]
{
    new PropertyBinding { EntityProperty = "Id", DtoProperty = "Id", Mode = BindingMode.TwoWay, IsPrimaryKey = true, PrimaryKeyOrder = 1 },
    new PropertyBinding { EntityProperty = "Name", DtoProperty = "CategoryName", Mode = BindingMode.TwoWay }
};

var tagBindings = new PropertyBinding[]
{
    new PropertyBinding { EntityProperty = "Id", DtoProperty = "Id", Mode = BindingMode.TwoWay },
    new PropertyBinding { EntityProperty = "Name", DtoProperty = "Name", Mode = BindingMode.TwoWay, IsPrimaryKey = true, PrimaryKeyOrder = 1 },
};

// declare classes
Class productDtoClass = assembly.DeclareClass("Product", typeof(Product), productBindings);
Class categoryDtoClass = assembly.DeclareClass("Category", typeof(Category), categoryBindings);
Class tagDtoClass = assembly.DeclareClass("Tag", typeof(Tag), tagBindings);

assembly.Build();

Type productDtoType = productDtoClass.GetBuiltType();
var product = JsonConvert.DeserializeObject<Product>(jsonData);
var productDto = Activator.CreateInstance(productDtoType);
Mapper.MapFromEntityToDto(productDto, product);
var resultJson = JsonConvert.SerializeObject(productDto);
```
#### resultJson
``` javascript
{
	id: 1,
	code: 'P01',
	name: 'Test Product',
	price: 20.5,
	external: {
		code: 'P01PHONE',
		information: 'Product information'
	},
	sku: 'VN_HCM',
	categoryName: 'Mobile',
	category: {
		id: 1220,
		categoryName: 'Mobile'
	},
	tags: [
		{
			id: 1,
			name: 'phone'
		},
		{
			id: 2,
			name: 'mobile'
		},
		{
			id: 3,
			name: 'mobile_phone'
		}
	]
}
```
#### Note
DTO -> Data Transfer Object
1. You can reverse the map by using Mapper.MapFromDtoToEntity(productDto, newProduct);
2. Binding Mode:
  - 2.1 TwoWay: it will create the property map from entity to DTO and reverse map (from dto to entity)
  - 2.2 OneWayToDto: it will create only one way map from entity to DTO (read only)  
  - 2.2 OneWayToEntity: it will create only one way map from DTO to entity (for update)
3. Use PrimaryKey = true, it means that property or properties are ids, you can get the ids by using DtoObject.GetIds(productDto) (return an object array)
4. All dto objects are implemented interface IDtoObject
5. For example above, you don't need to decare two classes Category and Tag if the dto structure of the classes are the same with entities 
6. All attributes of property of entity will copy to property of DTO
7. You will never worry about null exception when calling Mapper.MapFromDtoToEntity or Mapper.MapFromEntityToDto, it always check if null before mapping a property
8. You will never worry about performance because the class and the mapping are built by IL code

## Contacts
**[LinkedIn](https://www.linkedin.com/in/linh-le-258417105/)**
**Skype: linh.nhat.le**
