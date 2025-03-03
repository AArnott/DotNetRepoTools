// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestUpdateCommand : PullRequestModifyingCommandBase
{
	protected static readonly Option<string> TitleOption = new("--title", "The title of the pull request.");

	protected static readonly Option<string> DescriptionOption = new("--description", "The description of the pull request. If an argument for this option is not specified on the command line, it will be pulled in from STDIN.") { Arity = ArgumentArity.ZeroOrOne };

	protected static readonly Option<string> TargetBranchOption = new("--target-branch", "The target branch of the pull request. This MAY include the refs/heads/ prefix.");

	protected static readonly Option<bool?> AutoCompleteOption = new("--auto-complete", "Configures the pull request to be automatically completed when all policies are satisfied.");

	protected static readonly Option<GitPullRequestMergeStrategy?> MergeStrategyOption = new("--merge-strategy", "Specifies the merge strategy to use for auto-complete.");

	protected static readonly Option<bool?> DeleteSourceBranchOption = new("--delete-source-branch", "Whether to delete the source branch after the pull request is completed.");

	protected static readonly Option<PullRequestStatus?> StatusOption = new("--status", "Activates or abandons the pull request.");

	public PullRequestUpdateCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestUpdateCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.Title = invocationContext.ParseResult.GetValueForOption(TitleOption);
		this.Description = invocationContext.ParseResult.GetValueForOption(DescriptionOption);
		this.TargetBranch = invocationContext.ParseResult.GetValueForOption(TargetBranchOption);
		this.AutoComplete = invocationContext.ParseResult.GetValueForOption(AutoCompleteOption);
		this.MergeStrategy = invocationContext.ParseResult.GetValueForOption(MergeStrategyOption);
		this.DeleteSourceBranch = invocationContext.ParseResult.GetValueForOption(DeleteSourceBranchOption);
		this.Status = invocationContext.ParseResult.GetValueForOption(StatusOption);

		this.GetDescriptionFromStdIn = this.Description is null && invocationContext.ParseResult.Tokens.Any(t => DescriptionOption.HasAlias(t.Value));
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
		command.SetHandler(ctxt => new PullRequestUpdateCommand(ctxt).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
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
				this.Console.Error.WriteLine("Unable to determine who you are. Auto-complete cannot be set.");
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
				_ => throw new NotSupportedException($"Unsupported status: {status}"),
			};
		}

		if (body.Count == 0)
		{
			this.Console.WriteLine("SKIPPED. No changes requested. Add options to apply changes.");
			return;
		}

		HttpRequestMessage request = new(HttpMethod.Patch, "?api-version=7.1")
		{
			Content = JsonContent.Create(
				body,
				mediaType: new("application/json")),
		};

		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: true);
		if (this.IsSuccessResponse(response))
		{
			this.Console.WriteLine("OK");
		}
	}
}
