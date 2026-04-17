using Microsoft.Extensions.Options;
public class GithubToolFactory(GitHubAppClientFactory clientFactory, IOptions<GithubOptions> options)
{
	async Task<GitHubTool> CreateClient(RepoContext context)
	{
		var client = await clientFactory.CreateClient(options.Value.InstallationId);
		return new(client, context);
	}
}