using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
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

		var agent = chatClientFactory.CreateAgent(serviceOptions, tools);

		var prompt = BuildIssuePrompt(issue, repoContext, branchName, relativeIssueDirectory);
		logger.LogInformation("Running coding agent for issue #{IssueNumber}.", issue.Number);
		logger.LogDebug("Agent prompt: {Prompt}", prompt);

		await using var run = await InProcessExecution.RunStreamingAsync(
			agent,
			(List<ChatMessage>)[new(ChatRole.User, prompt)],
			cancellationToken: cancellationToken
		);
		if(!await run.TrySendMessageAsync(new TurnToken(emitEvents: true)))
			throw new Exception("Failed to send message to agent.");

		var result = "";
		await foreach (var evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
		{
			if (evt is AgentResponseUpdateEvent update)
				logger.LogDebug("[{UpdateExecutorId}]: {MessageText}", update.Update.AuthorName, string.Join(" ", update.Update.Text);
			else if (evt is WorkflowOutputEvent output)
			{
				// Workflow completed
				var conversationHistory = output.As<List<ChatMessage>>();
				result = (conversationHistory ?? []).Aggregate(result, (current, message) => current + (message.Text + "\n"));
				break;
			}
		}
		logger.LogDebug("Agent response: {Response}", result);
		return result;


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
			TASK:
			{issue.Title}
			{issue.Body ?? "(no description provided)"}
			If you ask what the task is, you are failing.
			Start implementing immediately.
			modify the code
			commit and push the changes to the branch {branchName}
			First step:
			Look for the relevant files in the repository.

			CONTEXT:
			- Repository root: {repoContext.RepoPath}
			- Working notes directory: {relativeIssueDirectory}
			- Preferred branch: {branchName}

			RULES:
			- Implement the required changes in the repository.
			- Run relevant checks/tests.
			- Commit changes with a clear message.
			- If useful, open a pull request.
			- Use tools to discover information, only ask for clarification if absolutely necessary.
			- Keep actions focused on this task only.
			- If you need to find a specific file or piece of code, use the file tool.
			- Modify the existing code using the file tool.
			- Dont ask any questions about the code structure, discover it yourself.
			- Make notes when you discover something useful.
			- This message contains the issue description and context, dont ask what you should do.


			""";
	}

}