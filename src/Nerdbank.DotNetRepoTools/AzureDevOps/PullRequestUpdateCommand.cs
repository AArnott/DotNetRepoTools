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

		this.GetDescriptionFromStdIn = this.Description is null && invocationContext.ParseResult.Tokens.Any(t => DescriptionOption.HasAlias(t.Value));
	}

	public string? Title { get; init; }

	public string? Description { get; set; }

	public bool GetDescriptionFromStdIn { get; init; }

	public string? TargetBranch { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("update", "Update a pull request.")
		{
			TitleOption,
			DescriptionOption,
			TargetBranchOption,
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
