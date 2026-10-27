using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AIAgent
{
    [McpServerToolType]
    internal class CalculatorTool
    {
        [McpServerTool, Description("Get sum of two numbers.")]
        public string Addition(int a, int b)
        {
            return $"Sum of {a} and {b} is {a + b}";
        }

        [McpServerTool, Description("Get difference of two numbers.")]
        public string Subtraction(int a, int b)
        {
            return $"Difference of {a} and {b} is SUB:{a - b}";
        }

        [McpServerTool, Description("Get division of two numbers.")]
        public string Division(int a, int b)
        {
            if (b == 0)
                return "Error: Division by zero is not allowed.";
            return $"Division of {a} by {b} is {a / b}";
        }

        [McpServerTool, Description("Get multiplication of two numbers.")]
        public string Multiplication(int a, int b)
        {
            return $"Multiplication of {a} and {b} is {a * b}";
        }
    }
}
