namespace ValidationPlatform.Core.Interfaces;

public interface IAccessibilityValidationProvider : IValidationProvider
{
    Task<AccessibilityAuditReport> AuditAccessibilityAsync();
}

public class AccessibilityAuditReport
{
    public bool                     Passed                 { get; set; } = true;
    public List<AccessibilityIssue> Issues                 { get; set; } = new();
    public int                      TotalElementsEvaluated { get; set; }
}

public class AccessibilityIssue
{
    public string ElementId       { get; set; } = string.Empty;
    public string ElementName     { get; set; } = string.Empty;
    public string ControlType     { get; set; } = string.Empty;
    public string RuleViolation   { get; set; } = string.Empty;
    public string Severity        { get; set; } = "Warning";
}
