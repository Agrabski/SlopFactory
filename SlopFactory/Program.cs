using SlopFactory.Services;
using SlopFactory.Tools;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<SlopServiceOptions>()
	.Bind(builder.Configuration.GetSection("SlopService"));
builder.Services.AddOptions<GithubOptions>()
	.Bind(builder.Configuration.GetSection("Github"));
builder.Services.AddOptions<GitHubAppOptions>()
	.Bind(builder.Configuration.GetSection("GitHubApp"));

builder.Services.AddSingleton<GitHubAppClientFactory>();
builder.Services.AddSingleton<GithubToolFactory>();
builder.Services.AddHostedService<SlopService>();

var app = builder.Build();


app.MapGet("/", () => "Hello World!");

app.Run();
