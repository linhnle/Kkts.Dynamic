using System;
using System.Diagnostics;
using System.Linq;
using Kkts.Dynamic;

public static class Program
{
    private const int Iterations = 100000;

    public static void Main()
    {
        var build = Stopwatch.StartNew();
        using (var assembly = new DynamicAssembly())
        {
            var cls = assembly.DeclareClass("ItemDto", typeof(Item), new[] {
                new PropertyBinding { EntityProperty = "Id", IsPrimaryKey = true },
                new PropertyBinding { EntityProperty = "Name" }
            });
            assembly.Build();
            var dtoType = cls.GetBuiltType();
            var dto = Activator.CreateInstance(dtoType);
            var entity = new Item { Id = 17, Name = "benchmark" };
            Console.WriteLine($"Declare/build/type creation: {build.Elapsed.TotalMilliseconds:F3} ms");

            var first = Stopwatch.StartNew();
            Mapper.MapFromEntityToDto(dto, entity);
            Console.WriteLine($"First mapping (delegate compilation included): {first.Elapsed.TotalMilliseconds:F3} ms");
            var method = dtoType.GetMethod(Class.InjectFromMethodName);
            Measure("Reflection dispatch", () => method.Invoke(dto, new object[] { entity }));
            Measure("Cached delegate dispatch", () => Mapper.MapFromEntityToDto(dto, entity));
            var entities = Enumerable.Range(0, 1000).Select(i => new Item { Id = i, Name = "item" }).ToArray();
            Measure("Map 1000-item collection", () => Mapper.ToDtoCollection(dtoType, entities), 100);
            if (!Equals(dto.GetIds()[0], entity.Id))
                throw new InvalidOperationException("Benchmark mapping produced an incorrect result.");
        }
    }

    private static void Measure(string name, Action action, int iterations = Iterations)
    {
        for (var i = 0; i < 1000; ++i) action();
        var timings = new double[5];
        var allocations = new double[5];
        for (var sample = 0; sample < timings.Length; ++sample)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < iterations; ++i) action();
            timer.Stop();
            timings[sample] = timer.Elapsed.TotalMilliseconds * 1000000 / iterations;
            allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
        }
        Array.Sort(timings);
        Array.Sort(allocations);
        Console.WriteLine($"{name}: median {timings[2]:F1} ns/op, {allocations[2]:F1} bytes/op");
    }

    public class Item
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
