using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
namespace SlopFactory.Tools;

public class ShellTool(RepoContext context) : IAIToolbox
{

	[Description("Execute a shell command inside the repository. Use for installs, builds, and tests.")]
	public async Task<string> Run(
		[Description("Shell command to execute inside the repository directory.")] string command)
	{
		var psi = new ProcessStartInfo
		{
			FileName = "/bin/bash",
			ArgumentList = {"-c", command },
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = context.RepoPath
		};

		var process = Process.Start(psi);
		var output = await process.StandardOutput.ReadToEndAsync();
		var error = await process.StandardError.ReadToEndAsync();

		return output + "\n" + error;
	}

	public IList<AITool> GetTools() =>
	[
		AIFunctionFactory.Create(Run)
	];
}
