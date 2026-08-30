using System.Windows.Automation;
using ValidationPlatform.Core.Interfaces;

namespace ValidationPlatform.Providers.Windows;

public class WindowsAccessibilityProvider : IAccessibilityValidationProvider
{
    private readonly AutomationElement? _rootElement;

    public string Name => "Windows UIA Accessibility Audit Provider";

    public WindowsAccessibilityProvider(AutomationElement? rootElement = null)
    {
        _rootElement = rootElement;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShutdownAsync()   => Task.CompletedTask;

    public Task<AccessibilityAuditReport> AuditAccessibilityAsync()
    {
        var root = _rootElement ?? AutomationElement.RootElement;
        var report = new AccessibilityAuditReport();

        if (root == null)
        {
            report.Passed = true;
            return Task.FromResult(report);
        }

        AuditElementRecursive(root, report);
        report.Passed = report.Issues.Count == 0;
        return Task.FromResult(report);
    }

    private void AuditElementRecursive(AutomationElement element, AccessibilityAuditReport report)
    {
        try
        {
            report.TotalElementsEvaluated++;
            var current = element.Current;

            // Rule 1: Buttons should have non-empty Name or AutomationId
            if (current.ControlType == ControlType.Button)
            {
                if (string.IsNullOrWhiteSpace(current.Name) && string.IsNullOrWhiteSpace(current.AutomationId))
                {
                    report.Issues.Add(new AccessibilityIssue
                                      {
                                          ElementId     = current.AutomationId
                                        , ElementName   = current.Name
                                        , ControlType   = current.ControlType.ProgrammaticName
                                        , RuleViolation = "Button is missing both accessible Name and AutomationId"
                                        , Severity      = "Error"
                                      });
                }
            }

            // Rule 2: Edit / Text inputs should have an accessible label or name
            if (current.ControlType == ControlType.Edit)
            {
                if (string.IsNullOrWhiteSpace(current.Name) && string.IsNullOrWhiteSpace(current.AutomationId) && string.IsNullOrWhiteSpace(current.HelpText))
                {
                    report.Issues.Add(new AccessibilityIssue
                                      {
                                          ElementId     = current.AutomationId
                                        , ElementName   = current.Name
                                        , ControlType   = current.ControlType.ProgrammaticName
                                        , RuleViolation = "Edit control lacks accessible name, help text, or AutomationId"
                                        , Severity      = "Warning"
                                      });
                }
            }

            var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                AuditElementRecursive(child, report);
            }
        }
        catch
        {
            // Transient UI exceptions ignored during tree traversal
        }
    }
}
