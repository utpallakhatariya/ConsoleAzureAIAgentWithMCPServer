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
var prompts = await mcpClient.ListPromptsAsync();
var resources = await mcpClient.ListResourcesAsync();

Console.WriteLine("Azure OpenAI client configured using Azure CLI credentials.");
Console.WriteLine("Connected to MCP server.");
Console.WriteLine("\nAvailable tools:");

foreach (var tool in tools)
{
    Console.WriteLine($"  {tool.Name}: {tool.Description}");
}

Console.WriteLine("\nAvailable prompts:");
foreach (var prompt in prompts)
{
    Console.WriteLine($"  {prompt.Name}: {prompt.Description}");
}

Console.WriteLine("\nAvailable resources:");
foreach (var resource in resources)
{
    Console.WriteLine($"  {resource.Uri}: {resource.Description}");
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
Console.WriteLine(
    "Use '/prompt <question>' to invoke the solve_question prompt.");
Console.WriteLine(
    "Use '/resource <question>' to read calculator://guide and use it as context.");

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

    var messageStartIndex = messages.Count;

    try
    {
        if (input.Equals("/prompt", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("/prompt ", StringComparison.OrdinalIgnoreCase))
        {
            var question = input["/prompt".Length..].Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                Console.WriteLine("Usage: /prompt <arithmetic question>");
                continue;
            }

            var prompt = await mcpClient.GetPromptAsync(
                "solve_question",
                new Dictionary<string, object?>
                {
                    ["question"] = question
                });

            Console.WriteLine(
                "Invoked MCP prompt 'solve_question'; sending its messages to Azure OpenAI.");

            foreach (var promptMessage in prompt.Messages)
            {
                var role = promptMessage.Role == ModelContextProtocol.Protocol.Role.Assistant
                    ? ChatRole.Assistant
                    : ChatRole.User;

                if (promptMessage.Content is not ModelContextProtocol.Protocol.TextContentBlock textContent)
                {
                    throw new InvalidOperationException(
                        "The solve_question prompt returned a non-text content block.");
                }

                messages.Add(new ChatMessage(role, textContent.Text));
            }
        }
        else if (input.Equals("/resource", StringComparison.OrdinalIgnoreCase) ||
                 input.StartsWith("/resource ", StringComparison.OrdinalIgnoreCase))
        {
            var question = input["/resource".Length..].Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                Console.WriteLine("Usage: /resource <question>");
                continue;
            }

            const string resourceUri = "calculator://guide";
            var resource = await mcpClient.ReadResourceAsync(resourceUri);
            var resourceText = string.Join(
                Environment.NewLine,
                resource.Contents.Select(content =>
                    content is ModelContextProtocol.Protocol.TextResourceContents textContent
                        ? textContent.Text
                        : throw new InvalidOperationException(
                            $"Resource '{resourceUri}' returned non-text content.")));

            Console.WriteLine(
                $"Read MCP resource '{resourceUri}'; sending its contents to Azure OpenAI as context.");
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"""
                Use this MCP resource as reference when answering the request.

                Resource ({resourceUri}):
                {resourceText}

                Request: {question}
                """));
        }
        else
        {
            messages.Add(new ChatMessage(ChatRole.User, input));
        }

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
        messages.RemoveRange(messageStartIndex, messages.Count - messageStartIndex);
        Console.WriteLine($"\nError: {ex.Message}");
    }
}
