using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;

namespace SlopFactory.Tools;

public class ShellTool(RepoContext context, ILogger<ShellTool> logger) : IAIToolbox
{
	private static readonly ActivitySource Source = new("SlopFactory.ShellTool");
	[Description("Execute a shell command inside the repository. Use for installs, builds, and tests.")]
	public async Task<string> Run(
		[Description("Shell command to execute inside the repository directory.")] string command)
	{
		using var activity = Source.StartActivity();
		logger.LogInformation("Executing shell command: {Command}", command);
		logger.LogDebug("Working directory: {WorkingDirectory}", context.RepoPath);

		var psi = new ProcessStartInfo
		{
			FileName = "/bin/bash",
			ArgumentList = { "-c", command },
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = context.RepoPath
		};

		try
		{
			using var process = Process.Start(psi);

			if (process == null)
			{
				logger.LogError("Failed to start process for command: {Command}", command);
				return "Failed to start process.";
			}

			var outputTask = process.StandardOutput.ReadToEndAsync();
			var errorTask = process.StandardError.ReadToEndAsync();

			await Task.WhenAll(outputTask, errorTask);
			await process.WaitForExitAsync();

			var output = outputTask.Result;
			var error = errorTask.Result;

			logger.LogInformation(
				"Command finished: {Command}, ExitCode: {ExitCode}, OutputLength: {OutputLength}, ErrorLength: {ErrorLength}",
				command,
				process.ExitCode,
				output?.Length ?? 0,
				error?.Length ?? 0
			);

			if (process.ExitCode != 0)
			{
				logger.LogWarning(
					"Command exited with non-zero code: {Command}, ExitCode: {ExitCode}",
					command,
					process.ExitCode
				);
			}

			return output + "\n" + error;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Exception while executing command: {Command}", command);
			return $"Error executing command: {ex.Message}";
		}
	}

	public IList<AITool> GetTools() =>
	[
		AIFunctionFactory.Create(Run)
	];
}