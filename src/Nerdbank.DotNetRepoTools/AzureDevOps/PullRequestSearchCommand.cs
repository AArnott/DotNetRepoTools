﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestSearchCommand : PullRequestCommandBase
{
	protected static readonly Option<string> SourceBranchOption = new("--source-branch", "The source branch of the pull request. MAY start with 'refs/heads/'.");

	protected static readonly Option<string> TargetBranchOption = new("--target-branch", "The target branch of the pull request. MAY start with 'refs/heads/'.");

	protected static readonly Option<PullRequestStatus?> StatusOption = new("--status", "The status of the pull request.");

	protected static readonly Option<int?> TopOption = new("--top", "The maximum number of pull requests to return.");

	protected static readonly Option<int?> SkipOption = new("--skip", "The number of pull requests to skip.");

	public PullRequestSearchCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestSearchCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.SourceBranch = invocationContext.ParseResult.GetValueForOption(SourceBranchOption);
		this.TargetBranch = invocationContext.ParseResult.GetValueForOption(TargetBranchOption);
		this.Status = invocationContext.ParseResult.GetValueForOption(StatusOption);
		this.Top = invocationContext.ParseResult.GetValueForOption(TopOption);
		this.Skip = invocationContext.ParseResult.GetValueForOption(SkipOption);
	}

	public string? SourceBranch { get; init; }

	public string? TargetBranch { get; init; }

	public PullRequestStatus? Status { get; init; }

	public int? Top { get; set; }

	public int? Skip { get; set; }

	internal static new Command CreateCommand()
	{
		Command command = new("search", "Search for pull requests.")
		{
			SourceBranchOption,
			TargetBranchOption,
			StatusOption,
			TopOption,
			SkipOption,
		};
		AddCommonOptions(command);

		command.SetHandler(ctxt => new PullRequestSearchCommand(ctxt).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		StringBuilder queryBuilder = new();
		queryBuilder.Append("?api-version=7.1");
		if (this.Status is not null)
		{
			queryBuilder.Append($"&searchCriteria.status={Uri.EscapeDataString(CamelCase(this.Status.ToString()!))}");
		}

		if (this.SourceBranch is not null)
		{
			queryBuilder.Append($"&searchCriteria.sourceRefName={Uri.EscapeDataString(PrefixRef("refs/heads/", this.SourceBranch))}");
		}

		if (this.TargetBranch is not null)
		{
			queryBuilder.Append($"&searchCriteria.targetRefName={Uri.EscapeDataString(PrefixRef("refs/heads/", this.TargetBranch))}");
		}

		if (this.Top is not null)
		{
			queryBuilder.Append($"&$top={this.Top}");
		}

		if (this.Skip is not null)
		{
			queryBuilder.Append($"&$skip={this.Skip}");
		}

		HttpRequestMessage request = new(HttpMethod.Get, queryBuilder.ToString());
		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: false);
		if (this.IsSuccessResponse(response))
		{
			// The result is an object with a "count" property and a "value" property.
			// The "value" property is a JSON array, and implicitly has a length, so just return the array.
			JsonNode? json = await response.Content.ReadFromJsonAsync<JsonNode>(this.CancellationToken);
			if (json?["value"] is JsonNode value)
			{
				this.Console.WriteLine(value.ToString());
			}
		}
		else
		{
			await this.PrintErrorMessageAsync(response);
		}
	}
}
