using Microsoft.IdentityModel.Tokens;
using Octokit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
public class GitHubAppClientFactory
{
	private readonly string _appId;
	private readonly string _privateKeyPem;

	public GitHubAppClientFactory(string appId, string privateKeyPem)
	{
		_appId = appId;
		_privateKeyPem = privateKeyPem;
	}

	public async Task<GitHubClient> CreateClient(long installationId)
	{
		var jwt = CreateJwt();

		// Step 1: authenticate as app
		var appClient = new GitHubClient(new ProductHeaderValue("coding-agent"))
		{
			Credentials = new Credentials(jwt, AuthenticationType.Bearer)
		};

		// Step 2: exchange for installation token
		var response = await appClient.GitHubApps.CreateInstallationToken(installationId);

		// Step 3: return authenticated client
		return new GitHubClient(new ProductHeaderValue("coding-agent"))
		{
			Credentials = new Credentials(response.Token)
		};
	}

	private string CreateJwt()
	{
		using var rsa = RSA.Create();
		rsa.ImportFromPem(_privateKeyPem.ToCharArray());

		var securityKey = new RsaSecurityKey(rsa);
		var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

		var now = DateTimeOffset.UtcNow;

		var token = new JwtSecurityToken(
			issuer: _appId,
			expires: now.AddMinutes(10).UtcDateTime,
			notBefore: now.AddMinutes(-1).UtcDateTime,
			signingCredentials: credentials
		);

		return new JwtSecurityTokenHandler().WriteToken(token);
	}
}