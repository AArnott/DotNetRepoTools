// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nerdbank.DotNetRepoTools.AzureDevOps;

[NotInParallel("CurrentDirectorySensitive")]
public class PullRequestCreateCommandTests : TestBase
{
	public string OriginalCurrentDirectory { get; } = Environment.CurrentDirectory;

	public override async ValueTask CleanupAsync()
	{
		Environment.CurrentDirectory = this.OriginalCurrentDirectory;
		await base.CleanupAsync();
	}

	[Test]
	public async Task DefaultsSourceAndTargetWhenOptionsAreOmitted()
	{
		string repoPath = await this.CreateGitRepoAsync("feature/test-pr");
		Environment.CurrentDirectory = repoPath;
		TestablePullRequestCreateCommand command = this.CreateCommand(sourceRefName: null, targetRefName: null);
		command.RepositoryDefaultBranch = "refs/heads/main";

		await this.ExecuteCommandAsync(command);

		Assert.Equal(0, command.ExitCode);
		JsonObject requestBody = this.GetPostedBody(command);
		Assert.Equal("https://dev.azure.com/fabrikam/Project/_apis/git/repositories/Repo?api-version=7.1", command.RepositoryRequestUri);
		Assert.Equal("refs/heads/feature/test-pr", requestBody["sourceRefName"]?.GetValue<string>());
		Assert.Equal("refs/heads/main", requestBody["targetRefName"]?.GetValue<string>());
	}

	[Test]
	public async Task ReportsErrorWhenSourceCannotBeInferred()
	{
		Directory.CreateDirectory(this.StagingDirectory);
		Environment.CurrentDirectory = this.StagingDirectory;
		TestablePullRequestCreateCommand command = this.CreateCommand(sourceRefName: null, targetRefName: "main");

		await this.ExecuteCommandAsync(command);

		Assert.Equal(1, command.ExitCode);
		Assert.Contains("Specify --source", ((StringWriter)command.Error).ToString(), StringComparison.Ordinal);
		Assert.Null(command.PostBody);
	}

	private JsonObject GetPostedBody(TestablePullRequestCreateCommand command)
	{
		Assert.NotNull(command.PostBody);
		return JsonNode.Parse(command.PostBody!)!.AsObject();
	}

	private TestablePullRequestCreateCommand CreateCommand(string? sourceRefName, string? targetRefName) => new()
	{
		Account = "fabrikam",
		CollectionUri = "https://dev.azure.com/fabrikam/",
		Error = new StringWriter(),
		Out = new StringWriter(),
		Project = "Project",
		Repo = "Repo",
		SourceRefName = sourceRefName,
		TargetRefName = targetRefName,
		Title = "Test title",
	};

	private async Task ExecuteCommandAsync(TestablePullRequestCreateCommand command)
	{
		this.MSBuild.SaveAll();
		try
		{
			await command.ExecuteAndDisposeAsync();
		}
		finally
		{
			if (command.Out is StringWriter outWriter && command.Error is StringWriter errorWriter)
			{
				this.DumpConsole(outWriter, errorWriter);
			}
		}

		this.MSBuild.ReloadEverything();
	}

	private async Task<string> CreateGitRepoAsync(string branchName)
	{
		string repoPath = Path.Combine(this.StagingDirectory, Path.GetRandomFileName());
		Directory.CreateDirectory(repoPath);
		await this.RunGitAsync(repoPath, "init");
		await this.RunGitAsync(repoPath, $"checkout -b {branchName}");
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
		string stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current!.Execution.CancellationToken);
		string stderr = await process.StandardError.ReadToEndAsync(TestContext.Current!.Execution.CancellationToken);
		await process.WaitForExitAsync(TestContext.Current!.Execution.CancellationToken);

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
