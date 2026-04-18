using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace SlopFactory.Services;

public class ChatClientFactory(ILoggerFactory loggerFactory) : IChatClientFactory
{
	public AIAgent CreateAgent(SlopServiceOptions options, IList<AITool> tools)
	{
		if (options.LlmProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
			return CreateOllamaAgent(options, tools);

		if (string.IsNullOrWhiteSpace(options.FoundryProjectEndpoint))
			throw new InvalidOperationException("SlopService:FoundryProjectEndpoint is not configured for Foundry provider.");

		throw new NotImplementedException();
	}


	private AIAgent CreateOllamaAgent(
		SlopServiceOptions options,
		IList<AITool> tools)
	{
		var apiKey = string.IsNullOrWhiteSpace(options.OllamaApiKey) ? "ollama" : options.OllamaApiKey;
		var chatClient = new ChatClient(
			options.Model,
			new ApiKeyCredential(apiKey),
			new OpenAIClientOptions
			{
				Endpoint = NormalizeOllamaEndpoint(options.OllamaUrl),
				NetworkTimeout = TimeSpan.FromMinutes(30)
			}
		);

		var aiChatClient = chatClient.AsIChatClient();
		var writingAgent = CreateAgent(
			options,
			tools,
			aiChatClient,
			"slopfactory-coder",
			"SlopFactoryCoder",
			"Writes what modifications should be made to the code base",
			);
		var analysisAgentOptions = new ChatClientAgentOptions
		{
			Id = "slopfactory-analyzer",
			Name = "Analyzer",
			Description = "Analyzes tasks laid before it and creates instructions",
			ChatOptions = new ChatOptions
			{
				Instructions = instructions,
				ModelId = options.Model,
				Tools = tools,
				AllowMultipleToolCalls = true,
				Reasoning = new()
				{
					Effort = ReasoningEffort.ExtraHigh,
					Output = ReasoningOutput.Full
				},
				ToolMode = new AutoChatToolMode(),
			},
		};

		var writerAgentOptions = new ChatClientAgentOptions
		{
			Id = "slopfactory-writer",
			Name = "Writer",
			Description = "Uses tools to ",
			ChatOptions = new ChatOptions
			{
				Instructions = instructions,
				ModelId = options.Model,
				Tools = tools,
				AllowMultipleToolCalls = true,
				Reasoning = new()
				{
					Effort = ReasoningEffort.ExtraHigh,
					Output = ReasoningOutput.Full
				},
				ToolMode = new AutoChatToolMode(),
			},
		};


		var builder = new WorkflowBuilder()
	}
	private ChatClientAgent CreateAgent(
		SlopServiceOptions options,
		IList<AITool> tools,
		IChatClient aiChatClient, 
		string? id,
		string? name,
		string? description,
		string? instructions
	)
	{

		var writingAgentOptions = new ChatClientAgentOptions
		{
			Id = id,
			Name = name,
			Description = description,
			ChatOptions = new()
			{
				Instructions = instructions,
				ModelId = options.Model,
				Tools = tools,
				AllowMultipleToolCalls = true,
				Reasoning = new()
				{
					Effort = ReasoningEffort.ExtraHigh,
					Output = ReasoningOutput.Full
				},
				ToolMode = new AutoChatToolMode(),
			},
		};
		var writingAgent = aiChatClient.AsAIAgent(writingAgentOptions, loggerFactory, null);
		return writingAgent;
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