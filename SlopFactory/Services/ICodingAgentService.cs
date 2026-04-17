using Octokit;
using SlopFactory.Tools;

namespace SlopFactory.Services;

public interface ICodingAgentService
{
	Task<string> ExecuteIssueTaskAsync(
		Issue issue,
		RepoContext repoContext,
		string branchName,
		string relativeIssueDirectory,
		GitHubClient githubClient,
		CancellationToken cancellationToken);
}
