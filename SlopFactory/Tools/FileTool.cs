using Microsoft.Extensions.AI;
using System.ComponentModel;
namespace SlopFactory.Tools;

public class FileTool(RepoContext context) : IAIToolbox
{

	private string Resolve(string relativePath)
	{
		var fullPath = Path.GetFullPath(Path.Combine(context.RepoPath, relativePath));

		if (!fullPath.StartsWith(context.RepoPath))
			throw new Exception("Access outside repository is not allowed.");

		return fullPath;
	}

	[Description("Read a file from the repository.")]
	public string Read(
		[Description("Path relative to the repository root.")] string path)
		=> File.ReadAllText(Resolve(path));

	[Description("Write a file inside the repository.")]
	public void Write(
		[Description("Path relative to the repository root.")] string path,
		[Description("Full file content.")] string content)
		=> File.WriteAllText(Resolve(path), content);

	[Description("Replace text inside a file in the repository.")]
	public void Patch(
		[Description("Path relative to the repository root.")] string path,
		[Description("Text to find.")] string find,
		[Description("Replacement text.")] string replace)
	{
		var fullPath = Resolve(path);
		var text = File.ReadAllText(fullPath);
		File.WriteAllText(fullPath, text.Replace(find, replace));
	}

	public IList<AITool> GetTools() =>
	[
		AIFunctionFactory.Create(Read),
		AIFunctionFactory.Create(Write),
		AIFunctionFactory.Create(Patch)
	];
}
