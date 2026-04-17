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
		var gitTool = new GitTool(repoContext);
		var shellTool = new ShellTool(repoContext);
		var gitHubTool = await githubToolFactory.CreateClient(repoContext);

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

		var response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
		return response.Text;

		Task<string> PushConfigured(string branch)
		{
			// Use the GitHub App installation token from the provided GitHub client.
			var pushToken = githubClient?.Credentials?.GetToken();

			if (string.IsNullOrWhiteSpace(pushToken))
			{
				return Task.FromResult("Push skipped: GitHub token is not configured.");
			}

			return gitTool.Push(branch, pushToken);
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

Task:
- Read and understand the issue.
- Implement the required changes in the repository.
- Run relevant checks/tests.
- Commit changes with a clear message.
- If useful, open a pull request.

Issue title:
{issue.Title}

Issue body:
{issue.Body ?? "(no description provided)"}

Context:
- Repository root: {repoContext.RepoPath}
- Working notes directory: {relativeIssueDirectory}
- Preferred branch: {branchName}

Important:
- If information is missing or ambiguous, call AskIssueQuestion so your question is posted to the issue.
- Do not ask for data you can discover via tools.
- Keep actions focused on this issue only.
""";
	}

}
