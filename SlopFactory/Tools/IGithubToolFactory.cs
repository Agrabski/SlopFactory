namespace SlopFactory.Tools;

public interface IGithubToolFactory
{
	Task<GitHubTool> CreateClient(RepoContext context);
}
