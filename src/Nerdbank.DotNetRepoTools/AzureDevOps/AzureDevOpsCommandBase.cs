// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Base type for Azure DevOps commands.
/// </summary>
public abstract class AzureDevOpsCommandBase : CommandBase
{
	/// <summary>
	/// Inferred Azure DevOps remote information from the local git repository's <c>origin</c> remote URL.
	/// Evaluated before option fields so we can determine whether options are required.
	/// </summary>
	private protected static readonly AzDoRemoteInfo? InferredRemoteInfo = AzDoRemoteInfo.TryInferFromGitRemote();

	private const string AzureDevOpsScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";

	private static readonly OptionOrEnvVar AccessTokenOption = new("--access-token", "SYSTEM_ACCESSTOKEN", isRequired: false, description: "The access token to use to authenticate against the AzDO REST API. If not specified but the SYSTEM_ACCESSTOKEN environment variable is set, that value will be used. Otherwise the tool will attempt to acquire a token automatically from Visual Studio or Windows credentials.", doNotAppendToDescription: true);

	private static readonly OptionOrEnvVar AccountOption = new("--account", "SYSTEM_COLLECTIONURI", isRequired: InferredRemoteInfo is null, "The AzDO account (organization) or URI (e.g. \"fabrikamfiber\" or \"https://dev.azure.com/fabrikamfiber/\". Can also be inferred from the git origin remote URL.");

	private static readonly OptionOrEnvVar ProjectOption = new("--project", "SYSTEM_TEAMPROJECT", isRequired: InferredRemoteInfo is null, "The AzDO project. Can also be inferred from the git origin remote URL.");

	private HttpClient? httpClient;
	private bool excludeManagedIdentityCredential;
	private bool automaticCredentialAttemptFailedDueToManagedIdentity;

	/// <summary>
	/// Initializes a new instance of the <see cref="AzureDevOpsCommandBase"/> class.
	/// </summary>
	protected AzureDevOpsCommandBase()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="AzureDevOpsCommandBase"/> class from parsed command-line data.
	/// </summary>
	/// <param name="parseResult">The parsed command-line result.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
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

	/// <summary>
	/// Gets the Azure DevOps access token, if one was explicitly supplied.
	/// </summary>
	public string? AccessToken { get; init; }

	/// <summary>
	/// Gets the collection URI (e.g. https://dev.azure.com/fabrikamfiber/).
	/// </summary>
	/// <value>
	/// A URI that is guaranteed to always end with a trailing slash.
	/// </value>
	public required string CollectionUri { get; init; }

	/// <summary>
	/// Gets the Azure DevOps account name.
	/// </summary>
	public required string Account { get; init; }

	/// <summary>
	/// Gets the Azure DevOps project name.
	/// </summary>
	public required string Project { get; init; }

	/// <summary>
	/// Gets the HTTP client used for Azure DevOps requests.
	/// </summary>
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

	/// <summary>
	/// Adds common Azure DevOps options to the specified command.
	/// </summary>
	/// <param name="command">The command to add options to.</param>
	protected static new void AddCommonOptions(Command command)
	{
		CommandBase.AddCommonOptions(command);
		command.Options.Add(AccessTokenOption);

		AccountOption.ApplyFallback(InferredRemoteInfo?.CollectionUri);
		command.Options.Add(AccountOption);

		ProjectOption.ApplyFallback(InferredRemoteInfo?.Project);
		command.Options.Add(ProjectOption);
	}

	/// <summary>
	/// Converts the first character of a string to lowercase.
	/// </summary>
	/// <param name="value">The value to transform.</param>
	/// <returns>The camel-cased value, or <see langword="null"/>.</returns>
	[return: NotNullIfNotNull(nameof(value))]
	protected static string? CamelCase(string? value)
	{
		if (value is null)
		{
			return null;
		}

		return value.Length == 0 ? string.Empty : (char.ToLower(value[0]) + value[1..]);
	}

	/// <summary>
	/// Adds a git ref prefix when the supplied ref name is not already fully qualified.
	/// </summary>
	/// <param name="defaultPrefix">The prefix to apply.</param>
	/// <param name="refName">The ref name to normalize.</param>
	/// <returns>The fully qualified ref name.</returns>
	protected static string PrefixRef(string defaultPrefix, string refName) => refName.StartsWith("refs/") ? refName : defaultPrefix + refName;

	/// <summary>
	/// Creates the HTTP client used for Azure DevOps requests.
	/// </summary>
	/// <returns>The HTTP client.</returns>
	protected virtual HttpClient CreateHttpClient()
	{
		HttpClient result = new()
		{
			BaseAddress = new Uri($"{this.CollectionUri}{this.Project}/_apis/"),
		};
		result.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		result.DefaultRequestHeaders.Add("X-TFS-FedAuthRedirect", "Suppress");

		string? accessToken = this.AccessToken ?? this.AcquireAccessTokenAutomatically(this.excludeManagedIdentityCredential);
		if (accessToken is not null)
		{
			result.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		}

		return result;
	}

	/// <summary>
	/// Attempts to automatically acquire an access token for Azure DevOps.
	/// </summary>
	/// <param name="excludeManagedIdentityCredential">A value indicating whether managed identity should be excluded from the credential chain for this attempt.</param>
	/// <returns>The access token, or null if it could not be acquired.</returns>
	protected virtual string? AcquireAccessTokenAutomatically(bool excludeManagedIdentityCredential)
	{
		this.automaticCredentialAttemptFailedDueToManagedIdentity = false;

		try
		{
			DefaultAzureCredential credential = excludeManagedIdentityCredential
				? new(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })
				: new();

			// Request an access token for Azure DevOps
			// The scope 499b84ac-1321-427f-aa17-267ca6975798 is the Azure DevOps application ID
			TokenRequestContext tokenRequestContext = new TokenRequestContext(new[] { AzureDevOpsScope });
			AccessToken token = credential.GetToken(tokenRequestContext, this.CancellationToken);

			return token.Token;
		}
		catch (Azure.Identity.CredentialUnavailableException ex)
		{
			this.automaticCredentialAttemptFailedDueToManagedIdentity = !excludeManagedIdentityCredential && IsManagedIdentityCredentialFailure(ex);

			// Log when credentials are unavailable if verbose mode is enabled
			if (this.Verbose)
			{
				this.Error.WriteLine($"No credentials available: {ex.Message}");
			}
		}
		catch (Azure.Identity.AuthenticationFailedException ex)
		{
			this.automaticCredentialAttemptFailedDueToManagedIdentity = !excludeManagedIdentityCredential && IsManagedIdentityCredentialFailure(ex);

			// Log authentication failures if verbose mode is enabled
			if (this.Verbose)
			{
				this.Error.WriteLine($"Failed to authenticate: {ex.Message}");
			}
		}

		return null;
	}

	/// <summary>
	/// Writes an HTTP request in the same format used by <c>--what-if</c> and verbose output.
	/// </summary>
	/// <param name="request">The request to describe.</param>
	/// <returns>A task that completes when the output has been written.</returns>
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
		if (await this.ShouldRetryWithoutManagedIdentityAsync(request, response))
		{
			response.Dispose();
			this.excludeManagedIdentityCredential = true;
			this.httpClient?.Dispose();
			this.httpClient = null;

			using HttpRequestMessage retryRequest = await this.CloneHttpRequestMessageAsync(request);
			if (this.Verbose)
			{
				this.Error.WriteLine("Retrying request without managed identity after an authentication failure.");
				await this.WriteWhatIfAsync(retryRequest);
			}

			response = await this.HttpClient.SendAsync(retryRequest);
		}

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

	/// <summary>
	/// Prints a human-readable error message for a failed response.
	/// </summary>
	/// <param name="response">The response to inspect.</param>
	/// <returns>A task that completes when the message has been written.</returns>
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
			else if (IsHtmlResponse(response))
			{
				this.Error.WriteLine("Authentication failed or Azure DevOps rejected the supplied credentials.");
				this.Error.WriteLine("Provide --access-token or run with --verbose to inspect credential acquisition.");

				if (this.Verbose)
				{
					this.Error.WriteLine(await response.Content.ReadAsStringAsync(this.CancellationToken));
				}
			}
			else
			{
				this.Error.WriteLine(await response.Content.ReadAsStringAsync(this.CancellationToken));
			}
		}
	}

	/// <summary>
	/// Determines whether a response represents a successful Azure DevOps call.
	/// </summary>
	/// <param name="response">The response to inspect.</param>
	/// <returns><see langword="true"/> if the response is successful; otherwise, <see langword="false"/>.</returns>
	protected virtual bool IsSuccessResponse([NotNullWhen(true)] HttpResponseMessage? response) => response is { IsSuccessStatusCode: true, StatusCode: not HttpStatusCode.NonAuthoritativeInformation };

	/// <summary>
	/// Gets the current user's Azure DevOps identity ID.
	/// </summary>
	/// <returns>The identity ID, or <see langword="null"/> if it could not be determined.</returns>
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

	/// <summary>
	/// Resolves an Azure DevOps identity by name.
	/// </summary>
	/// <param name="name">The identity name to search for.</param>
	/// <returns>The resolved identity ID, or <see langword="null"/> if no single match was found.</returns>
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

	private static bool IsAuthenticationFailureResponse(HttpResponseMessage response)
	{
		return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation;
	}

	private static bool IsManagedIdentityCredentialFailure(Exception exception)
	{
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			if (current.Message.Contains("ManagedIdentityCredential", StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsHtmlResponse(HttpResponseMessage response)
	{
		string? mediaType = response.Content.Headers.ContentType?.MediaType;
		return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
	}

	private async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage request)
	{
		HttpRequestMessage clone = new(request.Method, request.RequestUri)
		{
			Version = request.Version,
			VersionPolicy = request.VersionPolicy,
		};

		foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
		{
			clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		if (request.Content is not null)
		{
			byte[] contentBytes = await request.Content.ReadAsByteArrayAsync(this.CancellationToken);
			ByteArrayContent clonedContent = new(contentBytes);

			foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
			{
				clonedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
			}

			clone.Content = clonedContent;
		}

		return clone;
	}

	private async Task<bool> ShouldRetryWithoutManagedIdentityAsync(HttpRequestMessage request, HttpResponseMessage response)
	{
		if (this.excludeManagedIdentityCredential || this.AccessToken is not null || !this.automaticCredentialAttemptFailedDueToManagedIdentity || !IsAuthenticationFailureResponse(response))
		{
			return false;
		}

		if (this.Verbose)
		{
			this.Error.WriteLine($"The request to {new Uri(this.HttpClient.BaseAddress!, request.RequestUri!).AbsoluteUri} failed after a managed identity credential error.");
		}

		return true;
	}
}
