namespace SlopFactory.Tools;

public sealed class GitHubAppOptions
{
	public string AppId { get; set; } = string.Empty;
	// Path to the PEM file containing the GitHub App private key.
	public string PrivateKeyPemFile { get; set; } = string.Empty;
}