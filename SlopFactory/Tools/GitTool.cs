using System.ComponentModel;
public class GitTool(RepoContext context)
{
	private readonly ShellTool _shell = new(context);

	[Description("Create and switch to a new branch in the repository.")]
	public async Task<string> CreateBranch(
		[Description("Name of the branch to create.")] string name)
		=> await _shell.Run($"git checkout -b {name}");

	[Description("Stage all changes and commit.")]
	public async Task<string> Commit(
		[Description("Commit message describing the changes.")] string message)
		=> await _shell.Run($"git add . && git commit -m \"{message}\"");

	[Description("Push the current branch to the remote repository.")]
	public async Task<string> Push(
		[Description("Branch name to push.")] string branch,
		[Description("GitHub token for authentication.")] string token)
	{
		var remote = $"https://{token}@github.com/{context.Owner}/{context.Repo}.git";
		return await _shell.Run($"git push {remote} {branch}");
	}
}