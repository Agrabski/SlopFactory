using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace SlopFactory.Tools;

public class FileTool(RepoContext context, ILogger<FileTool> logger) : IAIToolbox
{
	private string Resolve(string relativePath)
	{
		var fullPath = Path.GetFullPath(Path.Combine(context.RepoPath, relativePath));

		if (!fullPath.StartsWith(context.RepoPath))
		{
			logger.LogWarning("Attempted access outside repository: {Path}", relativePath);
			throw new Exception("Access outside repository is not allowed.");
		}

		return fullPath;
	}

	[Description("Read a file from the repository.")]
	public string Read(
		[Description("Path relative to the repository root.")] string path)
	{
		var fullPath = Resolve(path);

		if (!File.Exists(fullPath))
		{
			logger.LogInformation("Read requested but file does not exist: {Path}", path);
			return string.Empty;
		}

		logger.LogInformation("Reading file: {Path}", path);
		return File.ReadAllText(fullPath);
	}

	[Description("Write a file inside the repository.")]
	public void Write(
		[Description("Path relative to the repository root.")] string path,
		[Description("Full file content.")] string content)
	{
		var fullPath = Resolve(path);

		logger.LogInformation("Writing file: {Path} (Length: {Length})", path, content?.Length ?? 0);
		File.WriteAllText(fullPath, content);
	}

	[Description("Replace text inside a file in the repository.")]
	public bool Patch(
		[Description("Path relative to the repository root.")] string path,
		[Description("Text to find.")] string find,
		[Description("Replacement text.")] string replace)
	{
		var fullPath = Resolve(path);

		if (!File.Exists(fullPath))
		{
			logger.LogWarning("Patch requested but file does not exist: {Path}", path);
			return false;
		}

		logger.LogInformation("Patching file: {Path}", path);

		var text = File.ReadAllText(fullPath);
		var occurrences = text.Split(find).Length - 1;

		File.WriteAllText(fullPath, text.Replace(find, replace));

		logger.LogInformation(
			"Patched file: {Path}, Replacements made: {Count}",
			path,
			occurrences
		);
		return true;
	}

	public IList<AITool> GetTools() =>
	[
		AIFunctionFactory.Create(Read),
		AIFunctionFactory.Create(Write),
		AIFunctionFactory.Create(Patch)
	];
}