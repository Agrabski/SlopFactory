using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Octokit;
using SlopFactory.Tools;

namespace SlopFactory.Services;

public class CodingAgentService(
	IOptionsMonitor<SlopServiceOptions> options,
	IGithubToolFactory githubToolFactory,
	IChatClientFactory chatClientFactory,
	IGitHubAppClientFactory gitHubAppClientFactory,
	ILogger<CodingAgentService> logger) : ICodingAgentService
{
	public async Task<string> ExecuteIssueTaskAsync(
		Issue issue,
		RepoContext repoContext,
		string branchName,
		string relativeIssueDirectory,
		GitHubClient githubClient,
		CancellationToken cancellationToken)
	{
		var serviceOptions = options.CurrentValue;

		var fileTool = new FileTool(repoContext);
		var shellTool = new ShellTool(repoContext);
		var gitHubTool = await githubToolFactory.CreateClient(repoContext);
		var gitTool = new GitTool(repoContext, (await gitHubAppClientFactory.CreateClient()).Credentials.GetToken());

		List<AITool> tools = [];
		tools.AddRange(fileTool.GetTools());
		tools.AddRange(gitTool.GetTools());
		tools.AddRange(shellTool.GetTools());
		tools.AddRange(gitHubTool.GetTools());
		tools.Add(AIFunctionFactory.Create(PushConfigured));
		tools.Add(AIFunctionFactory.Create(AskIssueQuestion));

		var instructions = string.IsNullOrWhiteSpace(serviceOptions.AgentInstructions)
			? "You are an autonomous coding agent. Complete the task using tools, and ask clarifying questions when blocked."
			: serviceOptions.AgentInstructions;

		AIAgent agent;
		try
		{
			agent = chatClientFactory.CreateAgent(serviceOptions, instructions, tools);
		}
		catch (InvalidOperationException ex)
		{
			return $"Coding agent skipped: {ex.Message}";
		}

		var prompt = BuildIssuePrompt(issue, repoContext, branchName, relativeIssueDirectory);
		logger.LogInformation("Running coding agent for issue #{IssueNumber}.", issue.Number);
		logger.LogDebug("Agent prompt: {Prompt}", prompt);

		var response = await agent.RunAsync(new ChatMessage(ChatRole.User, (string)prompt), cancellationToken: cancellationToken);
		logger.LogDebug("Agent response: {Response}", response.Text);
		return response.Text;


		Task<string> PushConfigured(string branch)
		{
			// Use the GitHub App installation token from the provided GitHub client.
			var pushToken = githubClient?.Credentials?.GetToken();

			if (string.IsNullOrWhiteSpace(pushToken))
			{
				return Task.FromResult("Push skipped: GitHub token is not configured.");
			}

			return gitTool.Push(branch);
		}

		async Task<string> AskIssueQuestion(string question)
		{
			if (string.IsNullOrWhiteSpace(question))
			{
				return "Question is empty. Provide a concrete question.";
			}

			var content = $"Agent question:\n\n{question}";
			await githubClient.Issue.Comment.Create(serviceOptions.RepoOwner, serviceOptions.RepoName, issue.Number, content);
			return "Question posted to issue comments.";
		}
	}
	private static string BuildIssuePrompt(
		Issue issue,
		RepoContext repoContext,
		string branchName,
		string relativeIssueDirectory)
	{
		return $"""
			You are working on GitHub issue #{issue.Number} in repository {repoContext.Owner}/{repoContext.Repo}.

			Context:
			- Repository root: {repoContext.RepoPath}
			- Working notes directory: {relativeIssueDirectory}
			- Preferred branch: {branchName}

			Important:
			- Implement the required changes in the repository.
			- Run relevant checks/tests.
			- Commit changes with a clear message.
			- If useful, open a pull request.
			- Use tools to discover information, only ask for clarification if absolutely necessary.
			- Keep actions focused on this task only.
			- If you need to find a specific file or piece of code, use the file tool.
			- Dont ask any questions about the code structure, discover it yourself.
			- Make notes when you discover something useful.
			- This message contains the issue description and context, dont ask what you should do.
			
			Your task that you should complete:
			{issue.Title}
			{issue.Body ?? "(no description provided)"}
			""";
	}

}