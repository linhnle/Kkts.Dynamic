using System.Collections.Generic;
using System.Reflection.Emit;

namespace Kkts.Dynamic.Internal
{
    internal class TreeEvaluationResult
    {
        public bool HasTheSameParent { get; set; }

        public List<MemberTree> Parents { get; set; }

        internal void BuildParentGuard(ILGenerator generator, OpCode loadSource, Label endIf)
        {
            if (!HasTheSameParent) return;
            generator.Emit(OpCodes.Nop);
            for (var i = 0; i < Parents.Count; ++i)
            {
                generator.Emit(loadSource);
                for (var j = 0; j <= i; ++j)
                {
                    var parent = Parents[j];
                    if (parent.IsProperty)
                        generator.Emit(OpCodes.Callvirt, parent.Property.GetMethod);
                    else
                        generator.Emit(OpCodes.Ldfld, parent.Field);
                }
                generator.Emit(OpCodes.Brfalse, endIf);
            }
        }
    }
}
