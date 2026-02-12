// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class BranchUnfavoriteCommand : BranchCommandBase
{
	protected static readonly Argument<string> BranchNameArgument = new("branch-name") { Description = "The name of the branch to remove from favorites.", Arity = ArgumentArity.ExactlyOne };

	public BranchUnfavoriteCommand()
	{
	}

	[SetsRequiredMembers]
	public BranchUnfavoriteCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.BranchName = parseResult.GetValue(BranchNameArgument)!;
	}

	public required string BranchName { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("unfavorite", "Removes a branch from favorites.")
		{
			BranchNameArgument,
		};
		AddCommonOptions(command);
		command.SetAction((parseResult, cancellationToken) => new BranchUnfavoriteCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		// Get the repository details to retrieve the repository ID
		GitRepository? repository = await this.GetRepositoryAsync();
		if (repository is null)
		{
			this.Error.WriteLine($"Unable to find repository '{this.Repo}'.");
			this.ExitCode = 1;
			return;
		}

		// Prepare the branch ref name (ensure it has the refs/heads/ prefix)
		string refName = PrefixRef("refs/heads/", this.BranchName);

		// Get the list of favorites to find the one matching this branch
		HttpRequestMessage getFavoritesRequest = new(HttpMethod.Get, $"git/favorites/refs?api-version=7.1-preview.1&repositoryId={repository.Id}");
		HttpResponseMessage? getFavoritesResponse = await this.SendAsync(getFavoritesRequest, canReadContent: false);
		if (getFavoritesResponse is null)
		{
			return;
		}

		if (!this.IsSuccessResponse(getFavoritesResponse))
		{
			this.ExitCode = (int)getFavoritesResponse.StatusCode;
			this.Error.WriteLine($"{(int)getFavoritesResponse.StatusCode} {getFavoritesResponse.StatusCode}");
			return;
		}

		AzDOArray<GitRefFavorite>? favorites = await getFavoritesResponse.Content.ReadFromJsonAsync(SourceGenerationContext.Default.AzDOArrayGitRefFavorite, this.CancellationToken);
		GitRefFavorite? favorite = favorites?.Value.FirstOrDefault(f => f.Name.Equals(refName, StringComparison.OrdinalIgnoreCase) && f.RepositoryId == repository.Id);

		if (favorite is null)
		{
			this.Error.WriteLine($"Branch '{this.BranchName}' is not marked as a favorite.");
			this.ExitCode = 2;
			return;
		}

		// Delete the favorite
		HttpRequestMessage deleteRequest = new(HttpMethod.Delete, $"git/favorites/refs/{favorite.Id}?api-version=7.1-preview.1");
		HttpResponseMessage? deleteResponse = await this.SendAsync(deleteRequest, canReadContent: false);
		if (deleteResponse is null)
		{
			return;
		}

		if (this.IsSuccessResponse(deleteResponse))
		{
			this.Out.WriteLine($"Branch '{this.BranchName}' removed from favorites.");
		}
		else
		{
			this.ExitCode = (int)deleteResponse.StatusCode;
			this.Error.WriteLine($"{(int)deleteResponse.StatusCode} {deleteResponse.StatusCode}");
			if (deleteResponse.Content.Headers.ContentType?.MediaType == "application/json")
			{
				ErrorResponseWithMessage? errorResponse = await deleteResponse.Content.ReadFromJsonAsync(SourceGenerationContext.Default.ErrorResponseWithMessage, this.CancellationToken);
				this.Error.WriteLine(errorResponse?.Message ?? string.Empty);
			}
			else
			{
				this.Error.WriteLine(await deleteResponse.Content.ReadAsStringAsync(this.CancellationToken));
			}
		}
	}

	/// <inheritdoc />
	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{this.CollectionUri}{this.Project}/_apis/");
		return client;
	}
}
