using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Kkts.Dynamic.Internal
{
    internal sealed class DtoMetadataPublication
    {
        private int _ready;
        internal bool IsReady => Volatile.Read(ref _ready) != 0;
        internal void Publish() => Volatile.Write(ref _ready, 1);
    }

    internal static class DtoMetadataRegistry
    {
        private static readonly ConditionalWeakTable<Type, Entry> Entries = new ConditionalWeakTable<Type, Entry>();

        internal static void Stage(DtoMetadataPublication publication, IEnumerable<DtoShapeMetadata> shapes,
            IEnumerable<Type> contextualTypes)
        {
            foreach (var shape in shapes)
                Entries.Add(shape.DtoType, new Entry(publication, shape));
            foreach (var type in contextualTypes)
                Entries.Add(type, new Entry(publication, null));
        }

        internal static bool TryGet(Type type, out DtoShapeMetadata metadata, out DtoMetadataDiagnostic diagnostic)
        {
            metadata = null;
            if (Entries.TryGetValue(type, out var entry) && entry.Publication.IsReady)
            {
                metadata = entry.Metadata;
                if (metadata != null)
                {
                    diagnostic = null;
                    return true;
                }
                diagnostic = new DtoMetadataDiagnostic("ContextualDtoType", null, null, null,
                    $"DTO type '{type.FullName}' is a contextual grouping or overlay layout, not an independent declared shape.",
                    "Obtain the owning root shape's metadata and inspect its effective nested layout.");
                return false;
            }
            diagnostic = new DtoMetadataDiagnostic("MetadataUnavailable", null, null, null,
                $"Type '{type.FullName}' has no successfully published declared-shape metadata.",
                "Use an explicitly declared DTO type from a completely successful DynamicAssembly.Build.");
            return false;
        }

        private sealed class Entry
        {
            internal Entry(DtoMetadataPublication publication, DtoShapeMetadata metadata)
            {
                Publication = publication;
                Metadata = metadata;
            }
            internal DtoMetadataPublication Publication { get; }
            internal DtoShapeMetadata Metadata { get; }
        }
    }
}
