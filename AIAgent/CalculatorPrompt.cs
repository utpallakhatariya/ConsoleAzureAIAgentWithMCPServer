using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AIAgent;

[McpServerPromptType]
public sealed class CalculatorPrompt
{
    [McpServerPrompt, Description("Create clear instructions for solving a calculator question.")]
    public static string SolveQuestion(
        [Description("The arithmetic question to solve.")] string question)
    {
        return $"""
            Solve this arithmetic question using the calculator tools when needed:
            {question}

            Show the calculation and explain the result briefly.
            """;
    }
}
