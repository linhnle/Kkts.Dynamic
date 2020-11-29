using System.Collections.Generic;

namespace Kkts.Dynamic.Internal
{
    internal class TreeEvaluationResult
    {
        public bool HasTheSameParent { get; set; }

        public List<MemberTree> Parents { get; set; }
    }
}
