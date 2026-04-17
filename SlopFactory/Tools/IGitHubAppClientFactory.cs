using Octokit;

namespace SlopFactory.Tools;

public interface IGitHubAppClientFactory
{
	Task<GitHubClient> CreateClient(long installationId);
}
