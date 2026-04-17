namespace SlopFactory.Tools;

public class GithubToolFactory(IGitHubAppClientFactory clientFactory) : IGithubToolFactory
{
	public async Task<GitHubTool> CreateClient(RepoContext context)
	{
		var client = await clientFactory.CreateClient();
		return new(client, context);
	}
}
