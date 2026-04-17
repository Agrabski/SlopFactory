using Microsoft.Extensions.Options;
namespace SlopFactory.Services;

public class SlopService(IOptions<SlopServiceOptions> options) : IHostedService
{

	public Task StartAsync(CancellationToken cancellationToken)
	{
		throw new NotImplementedException();
	}
	public Task StopAsync(CancellationToken cancellationToken)
	{
		throw new NotImplementedException();
	}
}

public sealed class SlopServiceOptions
{
	public Uri OllamaUrl { get; set; } = new("http://localhost:11434");
	public string Model { get; set; } = "llama3";
	public string GithubToken { get; set; }
}
