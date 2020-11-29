using System;
using System.Reflection;

namespace Kkts.Dynamic.Internal
{
    internal class AlternationInfo
    {
        public Class Cls;

        public Type Type;

        public Type EntityElementType;

        public SpecialPropertyType PropertyType;

        public ConstructorInfo AlternativeCtor;
    }
}
