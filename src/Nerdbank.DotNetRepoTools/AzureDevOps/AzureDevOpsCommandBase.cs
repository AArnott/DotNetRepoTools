// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class AzureDevOpsCommandBase : CommandBase
{
	protected static readonly Option<string> AccessTokenOption = new(["--access-token", "-t"], "The access token to use to authenticate against the AzDO REST API.");

	protected static readonly Option<string> AccountOption = new("--account", "The AzDO account (organization).") { IsRequired = true };

	protected static readonly Option<string> ProjectOption = new("--project", "The AzDO project.") { IsRequired = true };

	protected static readonly Option<string> RepoOption = new("--repo", "The name of the repo.") { IsRequired = true };

	private HttpClient? httpClient;

	protected AzureDevOpsCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected AzureDevOpsCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.AccessToken = invocationContext.ParseResult.GetValueForOption(AccessTokenOption);
		this.Account = invocationContext.ParseResult.GetValueForOption(AccountOption)!;
		this.Project = invocationContext.ParseResult.GetValueForOption(ProjectOption)!;
		this.Repo = invocationContext.ParseResult.GetValueForOption(RepoOption)!;
	}

	public string? AccessToken { get; init; }

	public required string Account { get; init; }

	public required string Project { get; init; }

	public required string Repo { get; init; }

	public HttpClient HttpClient => this.httpClient ??= this.CreateHttpClient();

	/// <summary>
	/// Creates the super-command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Command git = new("azdo", "Azure DevOps operations")
		{
			PullRequestCommandBase.Create(),
		};

		return git;
	}

	protected static new void AddCommonOptions(Command command)
	{
		CommandBase.AddCommonOptions(command);
		command.AddOption(AccessTokenOption);
		command.AddOption(AccountOption);
		command.AddOption(ProjectOption);
		command.AddOption(RepoOption);
	}

	protected virtual HttpClient CreateHttpClient()
	{
		HttpClient result = new()
		{
			BaseAddress = new Uri($"https://dev.azure.com/{this.Account}/{this.Project}/_apis/git/repositories/{this.Repo}/"),
		};
		if (this.AccessToken is not null)
		{
			result.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.AccessToken);
		}

		return result;
	}

	protected async Task WriteWhatIfAsync(HttpRequestMessage request)
	{
		this.Console.WriteLine($"{request.Method} {new Uri(this.HttpClient.BaseAddress!, request.RequestUri!).AbsoluteUri}");
		foreach (KeyValuePair<string, IEnumerable<string>> header in this.HttpClient.DefaultRequestHeaders)
		{
			this.Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
		}

		if (request.Content is not null)
		{
			this.Console.WriteLine(string.Empty);
			this.Console.WriteLine(await request.Content.ReadAsStringAsync(this.CancellationToken));
		}
	}
}
