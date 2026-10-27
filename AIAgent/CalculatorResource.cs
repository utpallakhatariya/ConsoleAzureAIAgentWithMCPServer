using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AIAgent;

[McpServerResourceType]
public sealed class CalculatorResource
{
    [McpServerResource(UriTemplate = "calculator://guide"),
     Description("Usage guide for the calculator tools.")]
    public static string GetCalculatorGuide()
    {
        return """
            Calculator tools:
            - addition(a, b): adds two integers.
            - subtraction(a, b): subtracts b from a.
            - division(a, b): divides a by b using integer division.
            - multiplication(a, b): multiplies two integers.

            Division by zero is not allowed.
            """;
    }
}
