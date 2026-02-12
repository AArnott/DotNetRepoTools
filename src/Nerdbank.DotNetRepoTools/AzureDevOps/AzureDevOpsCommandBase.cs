// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class AzureDevOpsCommandBase : CommandBase
{
	/// <summary>
	/// Inferred Azure DevOps remote information from the local git repository's <c>origin</c> remote URL.
	/// Evaluated before option fields so we can determine whether options are required.
	/// </summary>
	protected static readonly AzDoRemoteInfo? InferredRemoteInfo = AzDoRemoteInfo.TryInferFromGitRemote();

	protected static readonly OptionOrEnvVar AccessTokenOption = new("--access-token", "SYSTEM_ACCESSTOKEN", isRequired: false, description: "The access token to use to authenticate against the AzDO REST API. If not specified but the SYSTEM_ACCESSTOKEN environment variable is set, that value will be used. Otherwise the tool will attempt to acquire a token automatically from Visual Studio or Windows credentials.", doNotAppendToDescription: true);

	protected static readonly OptionOrEnvVar AccountOption = new("--account", "SYSTEM_COLLECTIONURI", isRequired: InferredRemoteInfo is null, "The AzDO account (organization) or URI (e.g. \"fabrikamfiber\" or \"https://dev.azure.com/fabrikamfiber/\". Can also be inferred from the git origin remote URL.");

	protected static readonly OptionOrEnvVar ProjectOption = new("--project", "SYSTEM_TEAMPROJECT", isRequired: InferredRemoteInfo is null, "The AzDO project. Can also be inferred from the git origin remote URL.");

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
			BranchCommandBase.CreateCommand(),
		};

		return git;
	}

	protected static new void AddCommonOptions(Command command)
	{
		CommandBase.AddCommonOptions(command);
		command.Options.Add(AccessTokenOption);

		AccountOption.ApplyFallback(InferredRemoteInfo?.CollectionUri);
		command.Options.Add(AccountOption);

		ProjectOption.ApplyFallback(InferredRemoteInfo?.Project);
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

		string? accessToken = this.AccessToken ?? this.AcquireAccessTokenAutomatically();
		if (accessToken is not null)
		{
			result.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		}

		return result;
	}

	/// <summary>
	/// Attempts to automatically acquire an access token for Azure DevOps.
	/// </summary>
	/// <returns>The access token, or null if it could not be acquired.</returns>
	protected virtual string? AcquireAccessTokenAutomatically()
	{
		try
		{
			// Use DefaultAzureCredential to automatically acquire credentials
			// This will try multiple sources including:
			// - Environment variables
			// - Managed Identity
			// - Visual Studio
			// - Azure CLI
			// - Azure PowerShell
			// - Interactive browser (if needed)
			DefaultAzureCredential credential = new DefaultAzureCredential();

			// Request an access token for Azure DevOps
			// The scope 499b84ac-1321-427f-aa17-267ca6975798 is the Azure DevOps application ID
			const string AzureDevOpsScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
			TokenRequestContext tokenRequestContext = new TokenRequestContext(new[] { AzureDevOpsScope });
			AccessToken token = credential.GetToken(tokenRequestContext, this.CancellationToken);

			return token.Token;
		}
		catch (Azure.Identity.CredentialUnavailableException ex)
		{
			// Log when credentials are unavailable if verbose mode is enabled
			if (this.Verbose)
			{
				this.Error.WriteLine($"No credentials available: {ex.Message}");
			}
		}
		catch (Azure.Identity.AuthenticationFailedException ex)
		{
			// Log authentication failures if verbose mode is enabled
			if (this.Verbose)
			{
				this.Error.WriteLine($"Failed to authenticate: {ex.Message}");
			}
		}

		return null;
	}

	protected async Task WriteWhatIfAsync(HttpRequestMessage request)
	{
		this.Out.WriteLine($"{request.Method} {new Uri(this.HttpClient.BaseAddress!, request.RequestUri!).AbsoluteUri}");
		foreach (KeyValuePair<string, IEnumerable<string>> header in this.HttpClient.DefaultRequestHeaders)
		{
			if (header.Key is "Authorization")
			{
				this.Out.WriteLine($"{header.Key}: [redacted]");
			}
			else
			{
				this.Out.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
			}
		}

		if (request.Content is not null)
		{
			this.Out.WriteLine(string.Empty);
			this.Out.WriteLine(await request.Content.ReadAsStringAsync(this.CancellationToken));
		}
	}

	/// <summary>
	/// Sends an HTTP request asynchronously and processes the response, optionally displaying verbose output and error
	/// information.
	/// </summary>
	/// <remarks>If the operation is in 'what-if' mode, the request is not sent and the method returns <see
	/// langword="null"/>. When verbose mode is enabled, the method writes detailed request and response information to the
	/// output streams. The exit code is set based on the response status if the request fails.</remarks>
	/// <param name="request">The HTTP request message to send. Cannot be null.</param>
	/// <param name="canReadContent">A value indicating whether the response content can be read and displayed. If <see langword="true"/>, verbose
	/// output and error messages may include the response content.</param>
	/// <returns>A task representing the asynchronous operation. The task result is an <see cref="HttpResponseMessage"/> containing
	/// the HTTP response, or <see langword="null"/> if the operation is a 'what-if' simulation.</returns>
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
