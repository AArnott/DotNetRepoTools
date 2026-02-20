// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestLinkCommand : PullRequestModifyingCommandBase
{
	protected static readonly Argument<int> WorkItemIdArgument = new("workitem-id") { Description = "The ID of the work item to link to the pull request.", Arity = ArgumentArity.ExactlyOne };

	public PullRequestLinkCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestLinkCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.WorkItemId = parseResult.GetValue(WorkItemIdArgument);
	}

	public required int WorkItemId { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("link", "Link a work item to a pull request.")
		{
			WorkItemIdArgument,
		};
		AddCommonOptions(command);
		command.SetAction((parseResult, cancellationToken) => new PullRequestLinkCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		using HttpRequestMessage getDetails = new(HttpMethod.Get, "?api-version=7.1");
		using HttpResponseMessage? detailsResponse = await this.SendAsync(getDetails, false);
		if (!this.IsSuccessResponse(detailsResponse))
		{
			await this.PrintErrorMessageAsync(detailsResponse);
			this.ExitCode = 1;
			return;
		}

		string? projectId = null, repoId = null;
		JsonNode? detailsJson = await detailsResponse.Content.ReadFromJsonAsync(SourceGenerationContext.Default.JsonNode, this.CancellationToken);
		if (detailsJson?["repository"]?["id"] is JsonValue repoIdValue)
		{
			repoId = repoIdValue.GetValue<string>();
		}

		if (detailsJson?["repository"]?["project"]?["id"] is JsonValue projectIdValue)
		{
			projectId = projectIdValue.GetValue<string>();
		}

		if (repoId is null || projectId is null)
		{
			this.Error.WriteLine("Unable to determine the project and repository IDs for this pull request. Work item will not be linked.");
			this.ExitCode = 2;
			return;
		}

		List<JsonPatch> patches = [
			new()
				{
					Path = "/relations/-",
					Value = new JsonObject
					{
						["rel"] = "ArtifactLink",
						["url"] = $"vstfs:///Git/PullRequestId/{projectId}%2F{repoId}%2F{this.PullRequestId}",
						["attributes"] = new JsonObject
						{
							["name"] = "Pull Request",
						},
					},
				},
			];

		HttpRequestMessage request = new(HttpMethod.Patch, $"{this.CollectionUri}{this.Project}/_apis/wit/workitems/{this.WorkItemId}?api-version=7.1")
		{
			Content = JsonContent.Create(
			patches,
			SourceGenerationContext.Default.ListJsonPatch,
			mediaType: new("application/json-patch+json")),
		};
		using HttpResponseMessage? response = await this.SendAsync(request, canReadContent: false);
		if (this.IsSuccessResponse(response))
		{
			this.Out.WriteLine($"Work item #{this.WorkItemId} linked to pull request {this.PullRequestId}.");
		}
		else
		{
			await this.PrintErrorMessageAsync(response);
		}
	}
}
