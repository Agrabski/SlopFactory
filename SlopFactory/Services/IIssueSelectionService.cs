using Octokit;

namespace SlopFactory.Services;

public interface IIssueSelectionService
{
	Task<IReadOnlyList<SelectedIssue>> SelectIssuesToStartAsync(CancellationToken cancellationToken);
}

public sealed record SelectedIssue(Issue Issue, string IssueDirectory, string RelativeIssueDirectory);
