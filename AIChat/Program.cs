using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using ModelContextProtocol.Client;

// Load settings from appsettings.json and environment variables.
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var endpoint = configuration["AzureOpenAI:Endpoint"];
var deployment = configuration["AzureOpenAI:DeploymentName"];

if (string.IsNullOrWhiteSpace(endpoint) ||
    string.IsNullOrWhiteSpace(deployment))
{
    throw new InvalidOperationException(
        "Configure AzureOpenAI:Endpoint and DeploymentName.");
}

// Create an Azure OpenAI chat client using Microsoft Entra ID.
IChatClient chatClient = new ChatClientBuilder(
        new AzureOpenAIClient(
                new Uri(endpoint),
                new AzureCliCredential())
            .GetChatClient(deployment)
            .AsIChatClient())
    .UseFunctionInvocation()
    .Build();

// Launch your existing MCP server over stdio.
var command = configuration["Mcp:Command"]
    ?? throw new InvalidOperationException(
        "Configure Mcp:Command.");

var arguments = configuration
    .GetSection("Mcp:Arguments")
    .GetChildren()
    .Select(argument => argument.Value ?? string.Empty)
    .ToArray();

var workingDirectory = configuration["Mcp:WorkingDirectory"];

var transport = new StdioClientTransport(
    new StdioClientTransportOptions
    {
        Name = "MyCalculatorMcp",
        Command = command,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        ShutdownTimeout = TimeSpan.FromSeconds(10)
    });

await using var mcpClient = await McpClient.CreateAsync(transport);

// Discover Addition and Subtraction (and any other available tools).
var tools = await mcpClient.ListToolsAsync();

Console.WriteLine("Azure OpenAI client configured using Azure CLI credentials.");
Console.WriteLine("Connected to MCP server.");
Console.WriteLine("\nAvailable tools:");

foreach (var tool in tools)
{
    Console.WriteLine($"  {tool.Name}: {tool.Description}");
}

// Retain the conversation across turns.
var messages = new List<ChatMessage>
{
    new(ChatRole.System,
        """
        You are a helpful calculator assistant.
        Use the available MCP tools for arithmetic calculations.
        Do not invent tool results.
        Explain the result clearly and concisely.
        """)
};

Console.WriteLine(
    "\nCalculator AI is ready. Type 'exit' to quit.");
Console.WriteLine(
    "Examples: Add 10 and 20 | Subtract 15 from 50");

while (true)
{
    Console.Write("\nYou: ");
    var input = Console.ReadLine();

    if (input is null ||
        input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(input))
        continue;

    messages.Add(new ChatMessage(ChatRole.User, input));

    try
    {
        var response = await chatClient.GetResponseAsync(
            messages,
            new ChatOptions
            {
                Tools = [.. tools]
            });

        // Includes the assistant response and tool-call/result
        // messages, so subsequent questions retain context.
        messages.AddRange(response.Messages);

        Console.WriteLine($"\nAI: {response.Text}");
    }
    catch (Exception ex)
    {
        // Remove the user turn if this request failed.
        messages.RemoveAt(messages.Count - 1);
        Console.WriteLine($"\nError: {ex.Message}");
    }
}
