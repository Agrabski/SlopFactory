using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Octokit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
namespace SlopFactory.Tools;

using System.IO;

public sealed class GitHubAppClientFactory(
	IOptions<GitHubAppOptions> appOptions,
	IOptions<GithubOptions> githubOptions) : IGitHubAppClientFactory, IDisposable
{
	private readonly string _appId = appOptions.Value.AppId;
	private readonly string _privateKeyPemFile = appOptions.Value.PrivateKeyPemFile;
	private readonly long _installationId = githubOptions.Value.InstallationId;
	private readonly RSA _rsa = CreateRsa(appOptions.Value);
	
	private static RSA CreateRsa(GitHubAppOptions githubOptionsValue)
	{
		var privateKeyPem = File.ReadAllText(githubOptionsValue.PrivateKeyPemFile);
		var rsa = RSA.Create();
		rsa.ImportFromPem(privateKeyPem.ToCharArray());
		return rsa;
	}

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
		var securityKey = new RsaSecurityKey(_rsa);
		var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

		var now = DateTimeOffset.UtcNow;
		var iat = now.ToUnixTimeSeconds();
		var exp = now.AddMinutes(10).ToUnixTimeSeconds();

		// Prefer a numeric iss when possible per GitHub's expectations.
		object issValue = _appId;
		if (long.TryParse(_appId, out var parsed))
		{
			issValue = parsed;
		}

		var header = new JwtHeader(credentials);
		var payload = new JwtPayload
		{
			{ "iat", iat },
			{ "exp", exp },
			{ "iss", issValue }
		};

		var token = new JwtSecurityToken(header, payload);
		return new JwtSecurityTokenHandler().WriteToken(token);
	}
	public void Dispose()
	{
		_rsa.Dispose();
	}
}
