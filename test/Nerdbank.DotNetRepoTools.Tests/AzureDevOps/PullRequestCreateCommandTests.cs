// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nerdbank.DotNetRepoTools.AzureDevOps;

[Collection(nameof(CurrentDirectorySensitiveTestCollection))]
public class PullRequestCreateCommandTests : TestBase
{
	private readonly string originalCurrentDirectory = Environment.CurrentDirectory;
	private TestablePullRequestCreateCommand? command;

	public PullRequestCreateCommandTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	public override async ValueTask DisposeAsync()
	{
		Environment.CurrentDirectory = this.originalCurrentDirectory;
		if (this.command is not null)
		{
			this.DumpConsole((StringWriter)this.command.Out, (StringWriter)this.command.Error);
			this.command.Dispose();
		}

		await base.DisposeAsync();
	}

	[Fact]
	public async Task DefaultsSourceAndTargetWhenOptionsAreOmitted()
	{
		string repoPath = await this.CreateGitRepoAsync("feature/test-pr");
		Environment.CurrentDirectory = repoPath;
		this.command = this.CreateCommand(sourceRefName: null, targetRefName: null);
		this.command.RepositoryDefaultBranch = "refs/heads/main";

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.command.ExitCode);
		JsonObject requestBody = this.GetPostedBody();
		Assert.Equal("https://dev.azure.com/fabrikam/Project/_apis/git/repositories/Repo?api-version=7.1", this.command.RepositoryRequestUri);
		Assert.Equal("refs/heads/feature/test-pr", requestBody["sourceRefName"]?.GetValue<string>());
		Assert.Equal("refs/heads/main", requestBody["targetRefName"]?.GetValue<string>());
	}

	[Fact]
	public async Task ReportsErrorWhenSourceCannotBeInferred()
	{
		Directory.CreateDirectory(this.StagingDirectory);
		Environment.CurrentDirectory = this.StagingDirectory;
		this.command = this.CreateCommand(sourceRefName: null, targetRefName: "main");

		await this.ExecuteCommandAsync();

		Assert.Equal(1, this.command.ExitCode);
		Assert.Contains("Specify --source", ((StringWriter)this.command.Error).ToString(), StringComparison.Ordinal);
		Assert.Null(this.command.PostBody);
	}

	private TestablePullRequestCreateCommand CreateCommand(string? sourceRefName, string? targetRefName) => new()
	{
		Account = "fabrikam",
		CollectionUri = "https://dev.azure.com/fabrikam/",
		Project = "Project",
		Repo = "Repo",
		SourceRefName = sourceRefName,
		TargetRefName = targetRefName,
		Title = "Test title",
	};

	private async Task ExecuteCommandAsync()
	{
		Assert.NotNull(this.command);
		this.command.Out = new StringWriter();
		this.command.Error = new StringWriter();
		this.MSBuild.SaveAll();
		await this.command.ExecuteAndDisposeAsync();
		this.MSBuild.ReloadEverything();
	}

	private JsonObject GetPostedBody()
	{
		Assert.NotNull(this.command);
		Assert.NotNull(this.command.PostBody);
		return JsonNode.Parse(this.command.PostBody!)!.AsObject();
	}

	private async Task<string> CreateGitRepoAsync(string branchName)
	{
		string repoPath = Path.Combine(this.StagingDirectory, Path.GetRandomFileName());
		Directory.CreateDirectory(repoPath);
		await RunGitAsync(repoPath, "init");
		await RunGitAsync(repoPath, $"checkout -b {branchName}");
		return repoPath;
	}

	private async Task RunGitAsync(string workingDirectory, string arguments)
	{
		ProcessStartInfo startInfo = new("git", arguments)
		{
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			WorkingDirectory = workingDirectory,
		};

		using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to spawn git.");
		string stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		string stderr = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
		await process.WaitForExitAsync(TestContext.Current.CancellationToken);

		Assert.True(process.ExitCode == 0, $"git {arguments} failed.{Environment.NewLine}{stdout}{stderr}");
	}

	internal sealed class TestablePullRequestCreateCommand : PullRequestCreateCommand
	{
		internal string? PostBody { get; private set; }

		internal string? RepositoryDefaultBranch { get; set; }

		internal string? RepositoryRequestUri { get; private set; }

		protected override async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, bool canReadContent)
		{
			if (request.Method == HttpMethod.Get)
			{
				this.RepositoryRequestUri = request.RequestUri?.AbsoluteUri;
				return new(HttpStatusCode.OK)
				{
					Content = new StringContent(
						$$"""
						{
						  "id": "{{Guid.NewGuid()}}",
						  "url": "https://dev.azure.com/fabrikam/Project/_apis/git/repositories/Repo",
						  "webUrl": "https://dev.azure.com/fabrikam/Project/_git/Repo",
						  "defaultBranch": {{FormatJsonString(this.RepositoryDefaultBranch)}}
						}
						""",
						Encoding.UTF8,
						"application/json"),
				};
			}

			if (request.Method == HttpMethod.Post)
			{
				this.PostBody = await request.Content!.ReadAsStringAsync(this.CancellationToken);
				return new(HttpStatusCode.Created)
				{
					Content = new StringContent(
						$$"""
						{
						  "pullRequestId": 123,
						  "repository": {
						    "id": "{{Guid.NewGuid()}}",
						    "url": "https://dev.azure.com/fabrikam/Project/_apis/git/repositories/Repo",
						    "webUrl": "https://dev.azure.com/fabrikam/Project/_git/Repo"
						  }
						}
						""",
						Encoding.UTF8,
						"application/json"),
				};
			}

			throw new InvalidOperationException($"Unexpected {request.Method} request.");
		}

		private static string FormatJsonString(string? value) => JsonSerializer.Serialize(value);
	}
}
