// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class BranchFavoriteCommand : BranchCommandBase
{
	protected static readonly Argument<string> BranchNameArgument = new("branch-name") { Description = "The name of the branch to mark as a favorite.", Arity = ArgumentArity.ExactlyOne };

	public BranchFavoriteCommand()
	{
	}

	[SetsRequiredMembers]
	public BranchFavoriteCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.BranchName = parseResult.GetValue(BranchNameArgument)!;
	}

	public required string BranchName { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("favorite", "Marks a branch as a favorite.")
		{
			BranchNameArgument,
		};
		AddCommonOptions(command);
		command.SetAction((parseResult, cancellationToken) => new BranchFavoriteCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
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

		// Create the favorite
		GitRefFavorite favorite = new()
		{
			Name = refName,
			RepositoryId = repository.Id,
			Type = "ref",
		};

		// Use project-level client for favorites API
		HttpRequestMessage requestMessage = new(HttpMethod.Post, $"git/favorites/refs?api-version=7.1-preview.1")
		{
			Content = JsonContent.Create(favorite, SourceGenerationContext.Default.GitRefFavorite),
		};

		HttpResponseMessage? response = await this.SendAsync(requestMessage, canReadContent: false);
		if (response is null)
		{
			return;
		}

		if (this.IsSuccessResponse(response))
		{
			if (response.Content.Headers.ContentType?.MediaType == "application/json")
			{
				GitRefFavorite? createdFavorite = await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.GitRefFavorite, this.CancellationToken);
				if (createdFavorite is not null)
				{
					this.Out.WriteLine($"Branch '{this.BranchName}' marked as favorite (ID: {createdFavorite.Id}).");
				}
			}
		}
		else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
		{
			this.Out.WriteLine($"Branch '{this.BranchName}' is already marked as favorite.");
		}
		else
		{
			this.ExitCode = (int)response.StatusCode;
			this.Error.WriteLine($"{(int)response.StatusCode} {response.StatusCode}");
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

	/// <inheritdoc />
	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{this.CollectionUri}{this.Project}/_apis/");
		return client;
	}
}
