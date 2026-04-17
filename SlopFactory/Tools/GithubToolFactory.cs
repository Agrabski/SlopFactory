using Microsoft.Extensions.Options;
namespace SlopFactory.Tools;

public class GithubToolFactory(IGitHubAppClientFactory clientFactory, IOptions<GithubOptions> options) : IGithubToolFactory
{
	public async Task<GitHubTool> CreateClient(RepoContext context)
	{
		var client = await clientFactory.CreateClient(options.Value.InstallationId);
		return new(client, context);
	}
}
