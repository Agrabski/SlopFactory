using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Octokit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
namespace SlopFactory.Tools;

using System.IO;

public class GitHubAppClientFactory(
	IOptions<GitHubAppOptions> appOptions,
	IOptions<GithubOptions> githubOptions) : IGitHubAppClientFactory
{
	private readonly string _appId = appOptions.Value.AppId;
	private readonly string _privateKeyPemFile = appOptions.Value.PrivateKeyPemFile;
	private readonly long _installationId = githubOptions.Value.InstallationId;

	public async Task<GitHubClient> CreateClient()
	{
		var jwt = CreateJwt();

		// Step 1: authenticate as app
		var appClient = new GitHubClient(new ProductHeaderValue("coding-agent"))
		{
			Credentials = new Credentials(jwt, AuthenticationType.Bearer)
		};

		// Step 2: exchange for installation token
		var response = await appClient.GitHubApps.CreateInstallationToken(_installationId);

		// Step 3: return authenticated client
		return new GitHubClient(new ProductHeaderValue("coding-agent"))
		{
			Credentials = new Credentials(response.Token)
		};
	}

	private string CreateJwt()
	{
		var privateKeyPem = File.ReadAllText(_privateKeyPemFile);
		using var rsa = RSA.Create();
		rsa.ImportFromPem(privateKeyPem.ToCharArray());

		var securityKey = new RsaSecurityKey(rsa);
		var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

		var now = DateTimeOffset.UtcNow;
		var issuedAt = now.UtcDateTime;
		var notBefore = now.AddMinutes(-1).UtcDateTime;
		var expiresAt = now.AddMinutes(10).UtcDateTime;

		var token = new JwtSecurityToken(
			issuer: _appId,
			issuedAt: issuedAt,
			notBefore: notBefore,
			expires: expiresAt,
			signingCredentials: credentials
		);

		return new JwtSecurityTokenHandler().WriteToken(token);
	}
}
