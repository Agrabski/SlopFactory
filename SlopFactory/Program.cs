using OpenTelemetry.Trace;
using SlopFactory.Services;
using SlopFactory.Tools;
var builder = WebApplication.CreateBuilder(args) ;
builder.Services.AddOpenTelemetry().WithTracing(t =>
{
	t.AddSource("*");
	t.SetSampler(new AlwaysOnSampler());

});
builder.Logging.AddOpenTelemetry(o =>
	{
		o.IncludeFormattedMessage = true;
		o.IncludeScopes = true;
	}
);

builder.Services.AddOptions<SlopServiceOptions>()
	.Bind(builder.Configuration.GetSection("SlopService"));
builder.Services.AddOptions<GithubOptions>()
	.Bind(builder.Configuration.GetSection("Github"));
builder.Services.AddOptions<GitHubAppOptions>()
	.Bind(builder.Configuration.GetSection("GitHubApp"));

builder.Services.AddSingleton<IGitHubAppClientFactory, GitHubAppClientFactory>();
builder.Services.AddSingleton<IGithubToolFactory, GithubToolFactory>();
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();
builder.Services.AddSingleton<IIssueSelectionService, IssueSelectionService>();
builder.Services.AddSingleton<ICodingAgentService, CodingAgentService>();
builder.Services.AddHostedService<SlopService>();

var app = builder.Build();


app.MapGet("/", () => "Hello World!");

app.Run();
