using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Dynamic
{
    public enum MappingDiagnosticSeverity { Error, Warning }

    public sealed class MappingDiagnostic
    {
        internal MappingDiagnostic(string code, string className, PropertyBinding binding,
            string direction, string message, string suggestedFix,
            MappingDiagnosticSeverity severity = MappingDiagnosticSeverity.Error, int? bindingIndex = null)
        {
            Code = code;
            ClassName = className;
            BindingIndex = binding?.OriginalIndex ?? bindingIndex;
            EntityPath = binding?.EntityProperty;
            DtoPath = binding == null ? null : binding.DtoProperty ?? binding.EntityProperty;
            Direction = direction;
            Message = message;
            SuggestedFix = suggestedFix;
            Severity = severity;
        }

        public string Code { get; }
        public MappingDiagnosticSeverity Severity { get; }
        public string ClassName { get; }
        public int? BindingIndex { get; }
        public string EntityPath { get; }
        public string DtoPath { get; }
        public string Direction { get; }
        public string Message { get; }
        public string SuggestedFix { get; }
    }

    public sealed class MappingValidationReport
    {
        internal MappingValidationReport(IEnumerable<MappingDiagnostic> diagnostics)
        {
            Diagnostics = Array.AsReadOnly(diagnostics
                .OrderBy(d => d.ClassName, StringComparer.Ordinal)
                .ThenBy(d => d.BindingIndex).ThenBy(d => d.Direction, StringComparer.Ordinal)
                .ThenBy(d => d.Code, StringComparer.Ordinal).ToArray());
        }

        public IReadOnlyList<MappingDiagnostic> Diagnostics { get; }
        public bool IsValid => Diagnostics.All(d => d.Severity != MappingDiagnosticSeverity.Error);
    }

    public sealed class MappingValidationException : InvalidOperationException
    {
        public MappingValidationException(MappingValidationReport report)
            : base(CreateMessage(report))
        {
            Report = report;
        }

        public MappingValidationReport Report { get; }

        private static string CreateMessage(MappingValidationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return "Mapping configuration is invalid: " + string.Join("; ", report.Diagnostics
                .Where(d => d.Severity == MappingDiagnosticSeverity.Error)
                .Select(d => $"{d.ClassName} [{d.DtoPath}] {d.Message} {d.SuggestedFix}"));
        }
    }
}
