using Microsoft.Extensions.Options;
namespace SlopFactory.Tools;

public class GithubToolFactory(GitHubAppClientFactory clientFactory, IOptions<GithubOptions> options)
{
	public async Task<GitHubTool> CreateClient(RepoContext context)
	{
		var client = await clientFactory.CreateClient(options.Value.InstallationId);
		return new(client, context);
	}
}