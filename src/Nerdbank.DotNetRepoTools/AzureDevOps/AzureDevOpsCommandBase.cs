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

	private HttpClient? httpClient;

	protected AzureDevOpsCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected AzureDevOpsCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
		this.AccessToken = parseResult.GetValue(AccessTokenOption);
		string account = parseResult.GetValue(AccountOption)!;
		this.Project = parseResult.GetValue(ProjectOption)!;

		if (account.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			this.CollectionUri = account;
			if (!this.CollectionUri.EndsWith('/'))
			{
				this.CollectionUri += "/";
			}

			Uri collectionUri = new(account);
			this.Account = collectionUri.Host == "dev.azure.com" ? collectionUri.PathAndQuery.TrimStart('/').TrimEnd('/') :
				collectionUri.Host.EndsWith(".visualstudio.com") ? collectionUri.Host.Substring(0, collectionUri.Host.IndexOf('.')) :
				throw new Exception($"Unrecognized collection URI pattern: {collectionUri.AbsoluteUri}");
		}
		else
		{
			this.CollectionUri = $"https://dev.azure.com/{account}/";
			this.Account = account;
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

	public required string Account { get; init; }

	public required string Project { get; init; }

	public HttpClient HttpClient => this.httpClient ??= this.CreateHttpClient();

	/// <summary>
	/// Creates the super-command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Command git = new("azdo", "Azure DevOps operations")
		{
			PullRequestCommandBase.CreateCommand(),
			WorkItemCommandBase.CreateCommand(),
		};

		return git;
	}

	protected static new void AddCommonOptions(Command command)
	{
		CommandBase.AddCommonOptions(command);
		command.Options.Add(AccessTokenOption);
		command.Options.Add(AccountOption);
		command.Options.Add(ProjectOption);
	}

	[return: NotNullIfNotNull(nameof(value))]
	protected static string? CamelCase(string? value)
	{
		if (value is null)
		{
			return null;
		}

		return value.Length == 0 ? string.Empty : (char.ToLower(value[0]) + value[1..]);
	}

	protected static string PrefixRef(string defaultPrefix, string refName) => refName.StartsWith("refs/") ? refName : defaultPrefix + refName;

	protected virtual HttpClient CreateHttpClient()
	{
		HttpClient result = new()
		{
			BaseAddress = new Uri($"{this.CollectionUri}{this.Project}/_apis/"),
		};
		if (this.AccessToken is not null)
		{
			result.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.AccessToken);
		}

		return result;
	}

	protected async Task WriteWhatIfAsync(HttpRequestMessage request)
	{
		this.Out.WriteLine($"{request.Method} {new Uri(this.HttpClient.BaseAddress!, request.RequestUri!).AbsoluteUri}");
		foreach (KeyValuePair<string, IEnumerable<string>> header in this.HttpClient.DefaultRequestHeaders)
		{
			this.Out.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
		}

		if (request.Content is not null)
		{
			this.Out.WriteLine(string.Empty);
			this.Out.WriteLine(await request.Content.ReadAsStringAsync(this.CancellationToken));
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
				this.Out.WriteLine(string.Empty);
				this.Out.WriteLine("RESPONSE:");
				this.Out.WriteLine(await response.Content.ReadAsStringAsync(this.CancellationToken));
			}
		}
		else
		{
			this.ExitCode = (int)response.StatusCode;
			this.Error.WriteLine($"{(int)response.StatusCode} {response.StatusCode}");
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
				this.Error.WriteLine(errorResponse?.Message ?? string.Empty);
			}
			else
			{
				this.Error.WriteLine(await response.Content.ReadAsStringAsync(this.CancellationToken));
			}
		}
	}

	protected virtual bool IsSuccessResponse([NotNullWhen(true)] HttpResponseMessage? response) => response is { IsSuccessStatusCode: true, StatusCode: not HttpStatusCode.NonAuthoritativeInformation };

	protected async Task<string?> WhoAmIAsync()
	{
		HttpRequestMessage request = new(HttpMethod.Get, "https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1");
		HttpResponseMessage? response = await this.SendAsync(request, false);
		if (this.IsSuccessResponse(response))
		{
			MeProfile? me = await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.MeProfile, this.CancellationToken);
			if (me is { Id: string id })
			{
				return id;
			}
		}

		return null;
	}

	protected async Task<string?> LookupIdentityAsync(string name)
	{
		HttpRequestMessage request = new(HttpMethod.Get, $"https://vssps.dev.azure.com/{this.Account}/_apis/identities?searchFilter=General&filterValue={Uri.EscapeDataString(name)}&queryMembership=None&api-version=6.0");
		HttpResponseMessage? response = await this.SendAsync(request, false);
		if (this.IsSuccessResponse(response))
		{
			AzDOArray<Identity>? result = await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.AzDOArrayIdentity, this.CancellationToken);
			if (result is { Value: [Identity only] })
			{
				return only.Id;
			}
		}

		return null;
	}
}
