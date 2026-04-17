using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Octokit;
using SlopFactory.Tools;
using System.Text.Json;
namespace SlopFactory.Services;

public class SlopService(
	IOptionsMonitor<SlopServiceOptions> options,
	IGitHubAppClientFactory gitHubAppClientFactory,
	IIssueSelectionService issueSelectionService,
	ICodingAgentService codingAgentService,
	ILogger<SlopService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Starting SlopService issue polling worker.");

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					await PollGithubAsync(cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Unhandled error while polling GitHub issues.");
				}

				var interval = NormalizePollInterval(options.CurrentValue.PollInterval);

				try
				{
					await Task.Delay(interval, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
			}
		}
		finally
		{
			logger.LogInformation("Stopping SlopService issue polling worker.");
		}
	}

	private async Task PollGithubAsync(CancellationToken cancellationToken)
	{
		var slopOptions = options.CurrentValue;
		var selectedIssues = await issueSelectionService.SelectIssuesToStartAsync(cancellationToken);
		if (selectedIssues.Count == 0)
		{
			return;
		}

		var client = await gitHubAppClientFactory.CreateClient();
		foreach (var selectedIssue in selectedIssues)
		{
			try
			{
				await StartIssueWorkAsync(
					client,
					selectedIssue.Issue,
					selectedIssue.IssueDirectory,
					slopOptions,
					cancellationToken);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to start work for issue #{IssueNumber}.", selectedIssue.Issue.Number);
			}
		}
	}

	private async Task StartIssueWorkAsync(
		GitHubClient client,
		Issue issue,
		string issueDir,
		SlopServiceOptions options,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(issueDir);

		var branchName = $"issue-{issue.Number}-{ToSlug(issue.Title)}";
		var repoContext = new RepoContext
		{
			RepoPath = issueDir,
			Owner = options.RepoOwner,
			Repo = options.RepoName,
			DefaultBranch = options.DefaultBranch
		};

		// Ensure local clone exists so tools that run in the repo directory can operate.
		var repoPathRoot = repoContext.RepoPath;
		var cloneToken = client?.Credentials?.GetToken();
		var cloneUrl = string.IsNullOrWhiteSpace(cloneToken)
			? $"https://github.com/{repoContext.Owner}/{repoContext.Repo}.git"
			: $"https://x-access-token:{cloneToken}@github.com/{repoContext.Owner}/{repoContext.Repo}.git";

		if (!Directory.Exists(issueDir) || !Directory.Exists(Path.Combine(issueDir, ".git")))
		{
			try
			{
				var parent = Path.GetDirectoryName(issueDir) ?? "/";
				Directory.CreateDirectory(parent);
				var psi = new ProcessStartInfo
				{
					FileName = "/bin/bash",
					ArgumentList = {"-c", $"git clone --depth 1 --branch {options.DefaultBranch} {cloneUrl} {repoPathRoot}" },
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					WorkingDirectory = parent
				};
				var proc = Process.Start(psi);
				var outText = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
				var errText = await proc.StandardError.ReadToEndAsync(cancellationToken);
				if (!proc.HasExited)
					proc.WaitForExit();
				if (proc.ExitCode != 0)
				{
					logger.LogWarning("Git clone returned non-zero exit code: {Out} {Err}", outText, errText);
				}
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to clone repository; continuing without clone.");
			}
		}

		var fileTool = new FileTool(repoContext);
		var gitTool = new GitTool(repoContext);

		var metadata = new
		{
			issue.Number,
			issue.Title,
			issue.HtmlUrl,
			issue.CreatedAt,
			StartedAtUtc = DateTimeOffset.UtcNow,
			branchName
		};

		fileTool.Write(
			$"{issueDir}/metadata.json",
			JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
		fileTool.Write(
			$"{issueDir}/notes.md",
			$"# Issue #{issue.Number}: {issue.Title}\n\n{issue.Body ?? "(no description)"}\n");

		var gitResult = await gitTool.CreateBranch(branchName);

			string pushResult = string.Empty;
			if (!string.IsNullOrWhiteSpace(cloneToken))
			{
				try
				{
					pushResult = await gitTool.Push(branchName, cloneToken);
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to push branch {Branch} to remote.", branchName);
				}
			}
			else
			{
				logger.LogWarning("No clone token available; skipping push of branch {Branch} to remote.", branchName);
			}

			var gitSummary = (gitResult + "\n" + pushResult).Trim();

		await client.Issue.Comment.Create(
			options.RepoOwner,
			options.RepoName,
			issue.Number,
			$"SlopFactory started working on this issue.\n\n- Branch: `{branchName}`\n- Workspace: `{issueDir}`");

		var agentResult = await codingAgentService.ExecuteIssueTaskAsync(
			issue,
			repoContext,
			branchName,
			issueDir,
			client,
			cancellationToken);

		logger.LogInformation(
			"Started work for issue #{IssueNumber} on branch {Branch}. Git output: {GitOutput}. Agent summary: {AgentSummary}",
			issue.Number,
			branchName,
			gitSummary,
			agentResult);

		cancellationToken.ThrowIfCancellationRequested();
	}

	private static string ToSlug(string value)
	{
		var chars = value
			.ToLowerInvariant()
			.Select(c => char.IsLetterOrDigit(c) ? c : '-')
			.ToArray();
		var raw = new string(chars);
		var compact = string.Join("-", raw.Split('-', StringSplitOptions.RemoveEmptyEntries));
		return compact.Length > 40 ? compact[..40] : compact;
	}

	private static TimeSpan NormalizePollInterval(TimeSpan configured)
	{
		var min = TimeSpan.FromSeconds(5);
		return configured < min ? min : configured;
	}
}

public sealed class SlopServiceOptions
{
	public Uri OllamaUrl { get; set; } = new("http://localhost:11434");
	public string Model { get; set; } = "llama3";

	public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
	public string IssueLabel { get; set; } = "slop";
	public string RepoPath { get; set; } = "/agent/workspace/repo";
	public string RepoOwner { get; set; } = "your-org";
	public string RepoName { get; set; } = "your-repo";
	public string DefaultBranch { get; set; } = "main";
	public string LlmProvider { get; set; } = "Foundry";
	public string FoundryProjectEndpoint { get; set; } = string.Empty;
	public string OllamaApiKey { get; set; } = "ollama";
	public string AgentInstructions { get; set; } = string.Empty;
}
