public class RepoContext
{
	public string RepoPath { get; init; } = "/agent/workspace/repo";
	public string Owner { get; init; } = "your-org";
	public string Repo { get; init; } = "your-repo";
	public string DefaultBranch { get; init; } = "main";
}