using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace SlopFactory.Services;

public class ChatClientFactory : IChatClientFactory
{
	public AIAgent CreateAgent(SlopServiceOptions options, string instructions, IList<AITool> tools)
	{
		if (options.LlmProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
			return CreateOllamaAgent(options, instructions, tools);

		if (string.IsNullOrWhiteSpace(options.FoundryProjectEndpoint))
			throw new InvalidOperationException(
				"SlopService:FoundryProjectEndpoint is not configured for Foundry provider.");

		return CreateFoundryAgent(options, instructions, tools);
	}

	private static AIAgent CreateFoundryAgent(
		SlopServiceOptions options,
		string instructions,
		IList<AITool> tools)
	{
		var projectClient = new AIProjectClient(
			new Uri(options.FoundryProjectEndpoint),
			new AzureCliCredential());

		return projectClient.AsAIAgent(
			model: options.Model,
			instructions: instructions,
			name: "SlopFactoryCoder",
			description: "Works GitHub issues by editing code and using git/github tools.",
			tools: tools);
	}

	private static AIAgent CreateOllamaAgent(
		SlopServiceOptions options,
		string instructions,
		IList<AITool> tools)
	{
		var apiKey = string.IsNullOrWhiteSpace(options.OllamaApiKey) ? "ollama" : options.OllamaApiKey;
		var chatClient = new ChatClient(
			options.Model,
			new ApiKeyCredential(apiKey),
			new OpenAIClientOptions
			{
				Endpoint = NormalizeOllamaEndpoint(options.OllamaUrl)
			});

		var aiChatClient = chatClient.AsIChatClient();
		var agentOptions = new ChatClientAgentOptions
		{
			Id = "slopfactory-coder",
			Name = "SlopFactoryCoder",
			Description = "Works GitHub issues by editing code and using git/github tools.",
			ChatOptions = new ChatOptions
			{
				Instructions = instructions,
				ModelId = options.Model,
				Tools = tools
			}
		};

		return aiChatClient.AsAIAgent(agentOptions, null, null);
	}

	private static Uri NormalizeOllamaEndpoint(Uri configured)
	{
		if (configured.AbsolutePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
		{
			return configured;
		}

		return new Uri(configured, "v1");
	}
}
