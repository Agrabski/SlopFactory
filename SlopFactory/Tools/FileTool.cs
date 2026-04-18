using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
namespace SlopFactory.Tools;

[Description("Provides safe repository-scoped file system operations including read, write, append, and text replacement (string and regex) with logging and telemetry.")]
public class FileEditTool(
    ILogger<FileEditTool> logger,
    RepoContext repo) : IAIToolbox
{
    private readonly ActivitySource _activitySource = new("AgentTools.FileEdit");

    // -------------------------
    // TOOL REGISTRATION
    // -------------------------
    public IList<AITool> GetTools()
    {
        return new List<AITool>
        {
            AIFunctionFactory.Create(ReadFileAsync),
            AIFunctionFactory.Create(WriteFileAsync),
            AIFunctionFactory.Create(AppendFileAsync),
            AIFunctionFactory.Create(ReplaceStringAsync),
            AIFunctionFactory.Create(ReplaceRegexAsync)
        };
    }

    // -------------------------
    // PATH RESOLUTION + SAFETY
    // -------------------------
    private string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(repo.RepoPath, path));

        var repoRoot = Path.GetFullPath(repo.RepoPath);

        if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path escapes repository boundary.");
        }

        return fullPath;
    }

    // -------------------------
    // READ FILE
    // -------------------------
    [Description("Reads a file within the repository.")]
    public async Task<string> ReadFileAsync(
        [Description("Relative file path inside repo")] string path)
    {
        using var activity = _activitySource.StartActivity("File.Read");

        try
        {
            var fullPath = ResolvePath(path);

            logger.LogInformation("Repo read: {Path}", fullPath);

            if (!File.Exists(fullPath))
                return $"ERROR: File not found: {path}";

            var content = await File.ReadAllTextAsync(fullPath);

            activity?.SetTag("repo.path", repo.RepoPath);
            activity?.SetTag("file.path", fullPath);
            activity?.SetTag("file.operation", "read");

            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ReadFile failed: {Path}", path);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return $"ERROR: {ex.Message}";
        }
    }

    // -------------------------
    // WRITE FILE
    // -------------------------
    [Description("Writes content to a file inside the repository.")]
    public async Task<string> WriteFileAsync(
        [Description("Relative file path")] string path,
        [Description("Content to write")] string content)
    {
        using var activity = _activitySource.StartActivity("File.Write");

        try
        {
            var fullPath = ResolvePath(path);

            logger.LogInformation("Repo write: {Path}", fullPath);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);

            activity?.SetTag("file.path", fullPath);
            activity?.SetTag("file.operation", "write");

            return "SUCCESS: File written.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WriteFile failed: {Path}", path);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return $"ERROR: {ex.Message}";
        }
    }

    // -------------------------
    // APPEND FILE
    // -------------------------
    [Description("Appends content to a file inside the repository.")]
    public async Task<string> AppendFileAsync(
        [Description("Relative file path")] string path,
        [Description("Content to append")] string content)
    {
        using var activity = _activitySource.StartActivity("File.Append");

        try
        {
            var fullPath = ResolvePath(path);

            logger.LogInformation("Repo append: {Path}", fullPath);

            await File.AppendAllTextAsync(fullPath, content, Encoding.UTF8);

            activity?.SetTag("file.path", fullPath);
            activity?.SetTag("file.operation", "append");

            return "SUCCESS: Content appended.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AppendFile failed: {Path}", path);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return $"ERROR: {ex.Message}";
        }
    }

    // -------------------------
    // STRING REPLACE
    // -------------------------
    [Description("Replaces literal strings inside a repository file.")]
    public async Task<string> ReplaceStringAsync(
        [Description("Relative file path")] string path,
        [Description("Search text")] string search,
        [Description("Replacement text")] string replace)
    {
        using var activity = _activitySource.StartActivity("File.Replace.String");

        try
        {
            var fullPath = ResolvePath(path);

            if (!File.Exists(fullPath))
                return $"ERROR: File not found: {path}";

            var content = await File.ReadAllTextAsync(fullPath);

            int count = content.Split(search).Length - 1;
            content = content.Replace(search, replace);

            await File.WriteAllTextAsync(fullPath, content);

            activity?.SetTag("file.path", fullPath);
            activity?.SetTag("replace.count", count);

            return $"SUCCESS: Replaced {count} occurrence(s).";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ReplaceString failed: {Path}", path);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return $"ERROR: {ex.Message}";
        }
    }

    // -------------------------
    // REGEX REPLACE
    // -------------------------
    [Description("Replaces regex matches inside a repository file.")]
    public async Task<string> ReplaceRegexAsync(
        [Description("Relative file path")] string path,
        [Description("Regex pattern")] string pattern,
        [Description("Replacement string")] string replacement,
        [Description("Regex options: IgnoreCase, Multiline, Singleline")] string options = "")
    {
        using var activity = _activitySource.StartActivity("File.Replace.Regex");

        try
        {
            var fullPath = ResolvePath(path);

            if (!File.Exists(fullPath))
                return $"ERROR: File not found: {path}";

            var content = await File.ReadAllTextAsync(fullPath);

            var regexOptions = RegexOptions.None;

            if (!string.IsNullOrWhiteSpace(options))
            {
                if (options.Contains("IgnoreCase", StringComparison.OrdinalIgnoreCase))
                    regexOptions |= RegexOptions.IgnoreCase;

                if (options.Contains("Multiline", StringComparison.OrdinalIgnoreCase))
                    regexOptions |= RegexOptions.Multiline;

                if (options.Contains("Singleline", StringComparison.OrdinalIgnoreCase))
                    regexOptions |= RegexOptions.Singleline;
            }

            var regex = new Regex(pattern, regexOptions);

            int count = regex.Matches(content).Count;

            var updated = regex.Replace(content, replacement);

            await File.WriteAllTextAsync(fullPath, updated, Encoding.UTF8);

            activity?.SetTag("file.path", fullPath);
            activity?.SetTag("regex.pattern", pattern);
            activity?.SetTag("regex.match_count", count);

            return $"SUCCESS: Replaced {count} match(es).";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ReplaceRegex failed: {Path}", path);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return $"ERROR: {ex.Message}";
        }
    }
}