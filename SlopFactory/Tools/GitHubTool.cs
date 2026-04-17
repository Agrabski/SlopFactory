using Octokit;
using System.ComponentModel;
public class GitHubTool(GitHubClient client, RepoContext context)
{

	[Description("Create a pull request for the repository.")]
	public async Task<string> CreatePR(string title, string head, string body)
	{
		var pr = new NewPullRequest(title, head, context.DefaultBranch)
		{
			Body = body
		};

		var result = await client.PullRequest.Create(
			context.Owner,
			context.Repo,
			pr
		);

		return result.HtmlUrl;
	}
}