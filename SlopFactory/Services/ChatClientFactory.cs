using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace SlopFactory.Services;

public class ChatClientFactory : IChatClientFactory
{
	private readonly ILoggerFactory _loggerFactory;

	public ChatClientFactory(ILoggerFactory loggerFactory)
	{
		_loggerFactory = loggerFactory;
	}

	public Workflow CreateAgent(SlopServiceOptions options, IList<AITool> tools)
	{
		if (options.LlmProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
			return CreateOllamaAgent(options, tools);

		if (string.IsNullOrWhiteSpace(options.FoundryProjectEndpoint))
			throw new InvalidOperationException("SlopService:FoundryProjectEndpoint is not configured for Foundry provider.");

		throw new NotImplementedException();
	}

	private Workflow CreateOllamaAgent(
		SlopServiceOptions options,
		IList<AITool> tools)
	{
		// Create the low-level chat client for Ollama
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

		// Instructions tailored for each role in the group chat
		string analyzerInstructions = "You are the Analyzer. Examine repository files, infer requirements, and produce a concise general plan outlining necessary edits and rationale.";
		string coderInstructions = "You are the Coder. Given the Analyzer's plan, decide concrete code edits, file paths, and minimal diffs required to implement the plan. Be explicit and unambiguous.";
		string toolUserInstructions = "You are the ToolUser. You execute tool actions (edit files, run commands, commit). Follow the Coder's edit instructions precisely and report results and diffs.";

		// Create three agents: analyzer, coder, and tool user
		var analyzerAgent = CreateAgent(options, tools, aiChatClient, "slopfactory-analyzer", "Analyzer", "Analyzes requirements and produces a general plan.", analyzerInstructions);
		var coderAgent = CreateAgent(options, tools, aiChatClient, "slopfactory-coder", "Coder", "Decides what edits to make based on the Analyzer plan.", coderInstructions);
		var toolUserAgent = CreateAgent(
			options,
			tools,
			aiChatClient,
			"slopfactory-tooluser",
			"ToolUser",
			"Uses repository tools to apply edits and create commits as instructed.",
			toolUserInstructions,
			ChatToolMode.RequireAny
		);

		// Note: the system that orchestrates multi-agent workflows may expect a single AIAgent or a Workflow.
		// Many callers use the returned AIAgent as the entrypoint; return the Analyzer as the primary representative
		// while having created the peer agents. If a Workflow/Orchestrator builder is available, it can be
		// extended here to register all three agents into a coordinated workflow.

		return AgentWorkflowBuilder.CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents)
				{
					MaximumIterationCount = 30
				}
			)
			.AddParticipants([analyzerAgent, coderAgent, toolUserAgent])
			.Build();
	}

	private AIAgent CreateAgent(
		SlopServiceOptions options,
		IList<AITool> tools,
		IChatClient aiChatClient,
		string id,
		string name,
		string description,
		string instructions,
		ChatToolMode? toolMode = null
	)
	{
		var agentOptions = new ChatClientAgentOptions
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
					Output = ReasoningOutput.Full,
				},
				ToolMode = toolMode ?? ChatToolMode.Auto,
			},
		};

		var agent = aiChatClient.AsAIAgent(agentOptions, _loggerFactory, null)
			.AsBuilder()
			.UseOpenTelemetry(sourceName:name)
			.Build();
		return agent;
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