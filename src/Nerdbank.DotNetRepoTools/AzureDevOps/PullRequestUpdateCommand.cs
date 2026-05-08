// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestUpdateCommand : PullRequestModifyingCommandBase
{
	protected static readonly Option<string> TitleOption = new("--title") { Description = "The title of the pull request." };

	protected static readonly Option<string> DescriptionOption = new("--description") { Description = "The description of the pull request. If an argument for this option is not specified on the command line, it will be pulled in from STDIN.", Arity = ArgumentArity.ZeroOrOne };

	protected static readonly Option<string> TargetBranchOption = new("--target-branch") { Description = "The target branch of the pull request. This MAY include the refs/heads/ prefix." };

	protected static readonly Option<bool?> AutoCompleteOption = new("--auto-complete") { Description = "Configures the pull request to be automatically completed when all policies are satisfied." };

	protected static readonly Option<GitPullRequestMergeStrategy?> MergeStrategyOption = new("--merge-strategy") { Description = "Specifies the merge strategy to use for auto-complete." };

	protected static readonly Option<bool?> DeleteSourceBranchOption = new("--delete-source-branch") { Description = "Whether to delete the source branch after the pull request is completed." };

	protected static readonly Option<PullRequestStatus?> StatusOption = new("--status") { Description = "Activates or abandons the pull request." };

	public PullRequestUpdateCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestUpdateCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.Title = parseResult.GetValue(TitleOption);
		this.Description = parseResult.GetValue(DescriptionOption);
		this.TargetBranch = parseResult.GetValue(TargetBranchOption);
		this.AutoComplete = parseResult.GetValue(AutoCompleteOption);
		this.MergeStrategy = parseResult.GetValue(MergeStrategyOption);
		this.DeleteSourceBranch = parseResult.GetValue(DeleteSourceBranchOption);
		this.Status = parseResult.GetValue(StatusOption);

		this.GetDescriptionFromStdIn = this.Description is null && parseResult.Tokens.Any(t => DescriptionOption.HasAlias(t.Value));
	}

	public string? Title { get; init; }

	public string? Description { get; set; }

	public bool GetDescriptionFromStdIn { get; init; }

	public string? TargetBranch { get; init; }

	public bool? AutoComplete { get; init; }

	public GitPullRequestMergeStrategy? MergeStrategy { get; init; } = GitPullRequestMergeStrategy.NoFastForward;

	public bool? DeleteSourceBranch { get; init; }

	public PullRequestStatus? Status { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("update", "Update a pull request.")
		{
			TitleOption,
			DescriptionOption,
			TargetBranchOption,
			AutoCompleteOption,
			MergeStrategyOption,
			DeleteSourceBranchOption,
			StatusOption,
		};
		AddCommonOptions(command, pullRequestIdAsArgument: true);
		command.SetAction((parseResult, cancellationToken) => new PullRequestUpdateCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		JsonObject? currentPRData = null;

		JsonObject body = new();

		if (this.Title is not null)
		{
			body["title"] = this.Title;
		}

		if (this.GetDescriptionFromStdIn)
		{
			this.Description = this.ReadFromStandardIn("Enter description for pull request.");
		}

		if (this.Description is not null)
		{
			body["description"] = this.Description;
		}

		if (this.TargetBranch is not null)
		{
			body["targetRefName"] = PrefixRef("refs/heads/", this.TargetBranch);
		}

		string? autoCompletedBy = null;
		if (this.AutoComplete is bool autoComplete)
		{
			autoCompletedBy = autoComplete ? await this.WhoAmIAsync() : "00000000-0000-0000-0000-000000000000";
			if (autoCompletedBy is null)
			{
				this.Error.WriteLine("Unable to determine who you are. Auto-complete cannot be set.");
			}
			else
			{
				body["autoCompleteSetBy"] = new JsonObject
				{
					["id"] = autoCompletedBy,
				};
			}
		}

		if (this.MergeStrategy is not null || this.DeleteSourceBranch is not null)
		{
			JsonObject completionOptions = new();
			if (this.MergeStrategy is GitPullRequestMergeStrategy mergeStrategy)
			{
				completionOptions["mergeStrategy"] = CamelCase(mergeStrategy.ToString());
			}

			if (this.DeleteSourceBranch is bool deleteSourceBranch)
			{
				completionOptions["deleteSourceBranch"] = deleteSourceBranch;
			}

			body["completionOptions"] = completionOptions;
		}

		if (this.Status is { } status)
		{
			body["status"] = status switch
			{
				PullRequestStatus.Active => "active",
				PullRequestStatus.Abandoned => "abandoned",
				PullRequestStatus.Completed => "completed",
				_ => throw new NotSupportedException($"Unsupported status: {status}"),
			};

			if (status == PullRequestStatus.Completed)
			{
				// We must also specify the lastMergeSourceCommit when completing/closing PRs.
				await GetCurrentPRDetailsAsync();
				if (currentPRData is null)
				{
					this.Error.WriteLine("Unable to determine current pull request details. Status will not be updated.");
					return;
				}

				body["lastMergeSourceCommit"] = new JsonObject { ["commitId"] = currentPRData["lastMergeSourceCommit"]!["commitId"]!.GetValue<string>() };
			}
		}

		if (body.Count == 0)
		{
			this.Out.WriteLine("SKIPPED. No changes requested. Add options to apply changes.");
			return;
		}

		HttpRequestMessage request = new(HttpMethod.Patch, "?api-version=7.1")
		{
			Content = JsonContent.Create(
				body,
				SourceGenerationContext.Default.JsonNode,
				mediaType: new("application/json")),
		};

		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: true);
		if (this.IsSuccessResponse(response))
		{
			this.Out.WriteLine("OK");
		}

		async ValueTask<JsonObject?> GetCurrentPRDetailsAsync()
		{
			if (currentPRData is not null)
			{
				return currentPRData;
			}

			HttpRequestMessage request = new(HttpMethod.Get, "?api-version=7.1");
			HttpResponseMessage? response = await this.SendAsync(request, canReadContent: false);
			if (this.IsSuccessResponse(response))
			{
				return currentPRData = (JsonObject?)JsonNode.Parse(await response.Content.ReadAsStringAsync(this.CancellationToken));
			}
			else
			{
				await this.PrintErrorMessageAsync(response);
				return null;
			}
		}
	}
}
