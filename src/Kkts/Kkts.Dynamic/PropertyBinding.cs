using Kkts.Dynamic.Internal;

namespace Kkts.Dynamic
{
    public class PropertyBinding
    {
        public string DtoProperty { get; set; }

        public string EntityProperty { get; set; }

        public BindingMode Mode { get; set; }

        public bool IsPrimaryKey { get; set; }

        public int PrimaryKeyOrder { get; set; }

        internal AlternationInfo Alternation;
    }
}
