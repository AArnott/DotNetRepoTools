// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class AzureDevOpsCommandBase : CommandBase
{
	protected static readonly OptionOrEnvVar AccessTokenOption = new("--access-token", "SYSTEM_ACCESSTOKEN", isRequired: true, description: "The access token to use to authenticate against the AzDO REST API.");

	protected static readonly OptionOrEnvVar AccountOption = new("--account", "SYSTEM_COLLECTIONURI", isRequired: true, "The AzDO account (organization) or URI (e.g. \"fabrikamfiber\" or \"https://dev.azure.com/fabrikamfiber/\".");

	protected static readonly OptionOrEnvVar ProjectOption = new("--project", "SYSTEM_TEAMPROJECT", isRequired: true, "The AzDO project.");

	protected static readonly OptionOrEnvVar RepoOption = new("--repo", "BUILD_REPOSITORY_NAME", isRequired: true, "The name of the repo.");

	private HttpClient? httpClient;

	protected AzureDevOpsCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected AzureDevOpsCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.AccessToken = invocationContext.ParseResult.GetValueForOption(AccessTokenOption);
		string account = invocationContext.ParseResult.GetValueForOption(AccountOption)!;
		this.Project = invocationContext.ParseResult.GetValueForOption(ProjectOption)!;
		this.Repo = invocationContext.ParseResult.GetValueForOption(RepoOption)!;

		if (account.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			this.CollectionUri = account;
			if (!this.CollectionUri.EndsWith('/'))
			{
				this.CollectionUri += "/";
			}
		}
		else
		{
			this.CollectionUri = $"https://dev.azure.com/{account}/";
		}
	}

	public string? AccessToken { get; init; }

	/// <summary>
	/// Gets the collection URI (e.g. https://dev.azure.com/fabrikamfiber/).
	/// </summary>
	/// <value>
	/// A URI that is guaranteed to always end with a trailing slash.
	/// </value>
	public required string CollectionUri { get; init; }

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

	protected static string CamelCase(string value)
	{
		return value.Length == 0 ? string.Empty : (char.ToLower(value[0]) + value[1..]);
	}

	protected virtual HttpClient CreateHttpClient()
	{
		HttpClient result = new()
		{
			BaseAddress = new Uri($"{this.CollectionUri}{this.Project}/_apis/git/repositories/{this.Repo}"),
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

	protected virtual async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, bool canReadContent)
	{
		if (this.WhatIf || this.Verbose)
		{
			await this.WriteWhatIfAsync(request);
			if (this.WhatIf)
			{
				return null;
			}
		}

		HttpResponseMessage response = await this.HttpClient.SendAsync(request);
		if (this.IsSuccessResponse(response))
		{
			if (this.Verbose && canReadContent)
			{
				this.Console.WriteLine(string.Empty);
				this.Console.WriteLine("RESPONSE:");
				this.Console.WriteLine(await response.Content.ReadAsStringAsync(this.CancellationToken));
			}
		}
		else
		{
			this.ExitCode = (int)response.StatusCode;
			this.Console.Error.WriteLine($"{(int)response.StatusCode} {response.StatusCode}");
			if (canReadContent)
			{
				await this.PrintErrorMessageAsync(response);
			}
		}

		return response;
	}

	protected virtual async Task PrintErrorMessageAsync(HttpResponseMessage? response)
	{
		if (response is null)
		{
			return;
		}

		if (!this.IsSuccessResponse(response))
		{
			if (response.Content.Headers.ContentType?.MediaType == "application/json")
			{
				ErrorResponseWithMessage? errorResponse = await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.ErrorResponseWithMessage, this.CancellationToken);
				this.Console.Error.WriteLine(errorResponse?.Message ?? string.Empty);
			}
			else
			{
				this.Console.Error.WriteLine(await response.Content.ReadAsStringAsync(this.CancellationToken));
			}
		}
	}

	protected virtual bool IsSuccessResponse([NotNullWhen(true)] HttpResponseMessage? response) => response is { IsSuccessStatusCode: true, StatusCode: not HttpStatusCode.NonAuthoritativeInformation };
}
