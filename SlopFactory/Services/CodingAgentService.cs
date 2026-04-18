using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Octokit;
using SlopFactory.Tools;
using System.Reflection;

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

		object responseObj = null;
		string responseText = null;
		try
		{
			responseObj = await InvokeAgentRunAsync(agent, prompt, cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Reflection RunAsync failed, falling back to original RunAsync string overload.");
			try
			{
				// Fallback: original string-based call
				var fallbackResp = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
				responseObj = fallbackResp;
			}
			catch (Exception ex2)
			{
				logger.LogError(ex2, "Agent RunAsync failed.");
				throw;
			}
		}

		if (responseObj != null)
		{
			var respType = responseObj.GetType();
			var textProp = respType.GetProperty("Text") ?? respType.GetProperty("text") ?? respType.GetProperty("Message") ?? respType.GetProperty("Content");
			if (textProp != null)
			{
				responseText = textProp.GetValue(responseObj)?.ToString();
			}
			else
			{
				responseText = responseObj.ToString();
			}
		}
		else
		{
			responseText = string.Empty;
		}

		logger.LogDebug("Agent response: {Response}", responseText);
		return responseText;

		async Task<object> InvokeAgentRunAsync(AIAgent agentInstance, string inputPrompt, CancellationToken ct)
		{
			var agentType = agentInstance.GetType();
			var runMethods = agentType.GetMethods().Where(m => m.Name == "RunAsync").ToArray();
			foreach (var method in runMethods)
			{
				var parameters = method.GetParameters();
				var args = new object[parameters.Length];
				var skip = false;
				for (int i = 0; i < parameters.Length; i++)
				{
					var p = parameters[i];
					var pType = p.ParameterType;
					if (pType == typeof(string))
					{
						args[i] = inputPrompt;
					}
					else if (pType == typeof(CancellationToken))
					{
						args[i] = ct;
					}
					else if (pType.IsArray)
					{
						var elemType = pType.GetElementType();
						var elem = CreateMessageInstance(elemType, inputPrompt);
						var arr = Array.CreateInstance(elemType, 1);
						arr.SetValue(elem, 0);
						args[i] = arr;
					}
					else if (pType.IsClass)
					{
						try
						{
							var inst = Activator.CreateInstance(pType);
							var prop = pType.GetProperty("Input") ?? pType.GetProperty("Prompt") ?? pType.GetProperty("Messages") ?? pType.GetProperty("Items") ?? pType.GetProperty("Content");
							if (prop != null)
							{
								if (prop.PropertyType == typeof(string))
									prop.SetValue(inst, inputPrompt);
								else if (prop.PropertyType.IsArray)
								{
									var elemType = prop.PropertyType.GetElementType();
									var elem = CreateMessageInstance(elemType, inputPrompt);
									var arr = Array.CreateInstance(elemType, 1);
									arr.SetValue(elem, 0);
									prop.SetValue(inst, arr);
								}
								else if (prop.PropertyType.IsGenericType)
								{
									var genArg = prop.PropertyType.GetGenericArguments().FirstOrDefault();
									if (genArg != null)
									{
										var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(genArg);
										var list = Activator.CreateInstance(listType);
										var add = listType.GetMethod("Add");
										var elem = CreateMessageInstance(genArg, inputPrompt);
										add.Invoke(list, new[] { elem });
										prop.SetValue(inst, list);
									}
								}
							}
							args[i] = inst;
						}
						catch
						{
							skip = true;
							break;
						}
					}
					else
					{
						skip = true;
						break;
					}
				}
				if (skip) continue;

				var invokeResult = method.Invoke(agentInstance, args);
				if (invokeResult is Task task)
				{
					await task.ConfigureAwait(false);
					if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
					{
						var resultProp = task.GetType().GetProperty("Result");
						return resultProp.GetValue(task);
					}
					return null;
				}
			}
			throw new InvalidOperationException("No compatible RunAsync overload found on agent.");
		}

		object CreateMessageInstance(Type msgType, string msgContent)
		{
			if (msgType == null) return null;
			var inst = Activator.CreateInstance(msgType);
			var contentProp = msgType.GetProperty("Content") ?? msgType.GetProperty("Text") ?? msgType.GetProperty("Message") ?? msgType.GetProperty("Input");
			if (contentProp != null && contentProp.PropertyType == typeof(string))
				contentProp.SetValue(inst, msgContent);
			var roleProp = msgType.GetProperty("Role") ?? msgType.GetProperty("Author") ?? msgType.GetProperty("RoleName");
			if (roleProp != null)
			{
				if (roleProp.PropertyType == typeof(string))
					roleProp.SetValue(inst, "user");
				else if (roleProp.PropertyType.IsEnum)
				{
					try { var enumVal = Enum.Parse(roleProp.PropertyType, "User", true); roleProp.SetValue(inst, enumVal); } catch { }
				}
			}
			return inst;
		}

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
