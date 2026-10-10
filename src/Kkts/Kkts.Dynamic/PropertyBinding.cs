using Kkts.Dynamic.Internal;
using System;
using System.Linq.Expressions;

namespace Kkts.Dynamic
{
    public class PropertyBinding
    {
        public string DtoProperty { get; set; }

        public string EntityProperty { get; set; }

        public BindingMode Mode { get; set; }

        public bool IsPrimaryKey { get; set; }

        public int PrimaryKeyOrder { get; set; }

        public string NestedShape { get; set; }

        public Type DtoValueType { get; set; }
        public LambdaExpression ToDtoExpression { get; set; }
        public LambdaExpression ToEntityExpression { get; set; }
        public Delegate ToDtoInMemory { get; set; }
        public Delegate ToEntityInMemory { get; set; }
        public LambdaExpression ComputedExpression { get; set; }
        public Delegate ComputedInMemory { get; set; }

        internal bool IsComputed => ComputedExpression != null || ComputedInMemory != null;
        internal bool IsTransformed => IsComputed || DtoValueType != null || ToDtoExpression != null
            || ToEntityExpression != null || ToDtoInMemory != null || ToEntityInMemory != null;
        internal AlternationInfo Alternation;
        internal int OriginalIndex;
    }
}
