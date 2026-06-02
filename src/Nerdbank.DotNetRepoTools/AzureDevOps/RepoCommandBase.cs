// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Base type for Azure DevOps commands that operate on a repository.
/// </summary>
public abstract class RepoCommandBase : AzureDevOpsCommandBase
{
	private static readonly OptionOrEnvVar RepoOption = new("--repo", "BUILD_REPOSITORY_NAME", isRequired: InferredRemoteInfo is null, "The name of the repo. Can also be inferred from the git origin remote URL.");

	/// <summary>
	/// Initializes a new instance of the <see cref="RepoCommandBase"/> class.
	/// </summary>
	protected RepoCommandBase()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="RepoCommandBase"/> class from parsed command-line data.
	/// </summary>
	/// <param name="parseResult">The parsed command-line result.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	[SetsRequiredMembers]
	protected RepoCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
		this.Repo = parseResult.GetValue(RepoOption)!;
	}

	/// <summary>
	/// Gets the repository name.
	/// </summary>
	public required string Repo { get; init; }

	/// <summary>
	/// Adds common repository options to the specified command.
	/// </summary>
	/// <param name="command">The command to add options to.</param>
	protected static new void AddCommonOptions(Command command)
	{
		AzureDevOpsCommandBase.AddCommonOptions(command);

		RepoOption.ApplyFallback(InferredRemoteInfo?.Repo);
		command.Options.Add(RepoOption);
	}

	/// <summary>
	/// Creates the HTTP client used for repository-scoped Azure DevOps requests.
	/// </summary>
	/// <returns>The HTTP client.</returns>
	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}git/repositories/{this.Repo}/");
		return client;
	}

	/// <summary>
	/// Gets the repository details by name.
	/// </summary>
	/// <returns>The repository details, or null if not found.</returns>
	private protected async Task<GitRepository?> GetRepositoryAsync()
	{
		Uri requestUri = new($"{this.CollectionUri}{Uri.EscapeDataString(this.Project)}/_apis/git/repositories/{Uri.EscapeDataString(this.Repo)}?api-version=7.1");
		HttpRequestMessage requestMessage = new(HttpMethod.Get, requestUri);
		HttpResponseMessage? response = await this.SendAsync(requestMessage, canReadContent: true);
		if (this.IsSuccessResponse(response))
		{
			return await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.GitRepository, this.CancellationToken);
		}

		return null;
	}
}
