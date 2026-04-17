using Microsoft.Extensions.Options;
using Octokit;
using SlopFactory.Tools;

namespace SlopFactory.Services;

public class IssueSelectionService(
	IOptionsMonitor<SlopServiceOptions> options,
	IGitHubAppClientFactory gitHubAppClientFactory,
	ILogger<IssueSelectionService> logger) : IIssueSelectionService
{
	public async Task<IReadOnlyList<SelectedIssue>> SelectIssuesToStartAsync(CancellationToken cancellationToken)
	{
		var slopOptions = options.CurrentValue;
		if (string.IsNullOrWhiteSpace(slopOptions.IssueLabel))
		{
			logger.LogWarning("Issue label is empty. Skipping poll cycle.");
			return Array.Empty<SelectedIssue>();
		}

		var client = await gitHubAppClientFactory.CreateClient();
		var request = new RepositoryIssueRequest
		{
			State = ItemStateFilter.Open,
			Filter = IssueFilter.All
		};
		request.Labels.Add(slopOptions.IssueLabel);

		var issues = await client.Issue.GetAllForRepository(slopOptions.RepoOwner, slopOptions.RepoName, request);
		var candidateIssues = issues.Where(i => i.PullRequest is null).ToList();
		var selectedIssues = new List<SelectedIssue>();

		foreach (var issue in candidateIssues)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var relativeIssueDir = Path.Combine(slopOptions.WorkRootDirectory, issue.Number.ToString())
				.Replace('\\', '/');
			var issueDir = Path.Combine(slopOptions.RepoPath, relativeIssueDir);

			if (Directory.Exists(issueDir))
			{
				continue;
			}

			selectedIssues.Add(new SelectedIssue(issue, issueDir, relativeIssueDir));
		}

		logger.LogInformation(
			"Found {TotalCount} open issues with label '{Label}' in {Owner}/{Repo}; selected {SelectedCount} new issues.",
			candidateIssues.Count,
			slopOptions.IssueLabel,
			slopOptions.RepoOwner,
			slopOptions.RepoName,
			selectedIssues.Count);

		return selectedIssues;
	}
}
