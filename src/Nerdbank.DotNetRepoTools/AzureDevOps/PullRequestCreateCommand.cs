// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestCreateCommand : PullRequestCommandBase
{
	protected static readonly Option<string> TitleOption = new("--title") { Description = "The title of the pull request.", Required = true };
	protected static readonly Option<string> DescriptionOption = new("--description") { Description = "The description of the pull request. If an argument for this option is not specified on the command line, it will be pulled in from STDIN.", Arity = ArgumentArity.ZeroOrOne };
	protected static readonly Option<string> SourceRefNameOption = new("--source") { Description = "The name of the branch to merge from. This should not include the refs/heads/ prefix.", Required = true };
	protected static readonly Option<string> TargetRefNameOption = new("--target") { Description = "The name of the branch to merge into. This should not include the refs/heads/ prefix.", Required = true };
	protected static readonly Option<bool> IsDraftOption = new("--draft") { Description = "Whether the pull request is a draft." };
	protected static readonly Option<string[]> LabelsOption = new("--labels") { Description = "Labels to apply to the pull request.", AllowMultipleArgumentsPerToken = true };

	public PullRequestCreateCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestCreateCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.Title = parseResult.GetValue(TitleOption)!;
		this.Description = parseResult.GetValue(DescriptionOption);
		this.SourceRefName = parseResult.GetValue(SourceRefNameOption)!;
		this.TargetRefName = parseResult.GetValue(TargetRefNameOption)!;
		this.IsDraft = parseResult.GetValue(IsDraftOption);
		this.Labels = parseResult.GetValue(LabelsOption);

		this.GetDescriptionFromStdIn = this.Description is null && parseResult.Tokens.Any(t => DescriptionOption.HasAlias(t.Value));
	}

	public required string Title { get; init; }

	public string? Description { get; set; }

	public bool GetDescriptionFromStdIn { get; init; }

	public required string SourceRefName { get; init; }

	public required string TargetRefName { get; init; }

	public bool IsDraft { get; init; }

	public string[]? Labels { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("create", "Creates a new pull request.")
		{
			TitleOption,
			DescriptionOption,
			SourceRefNameOption,
			TargetRefNameOption,
			IsDraftOption,
			LabelsOption,
		};
		AddCommonOptions(command);

		command.SetAction((parseResult, cancellationToken) => new PullRequestCreateCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		if (this.GetDescriptionFromStdIn)
		{
			this.Description = this.ReadFromStandardIn("Enter description for pull request.");
		}

		HttpRequestMessage requestMessage = new(HttpMethod.Post, "?api-version=6.0")
		{
			Content = JsonContent.Create(new
			{
				sourceRefName = $"refs/heads/{this.SourceRefName}",
				targetRefName = $"refs/heads/{this.TargetRefName}",
				title = this.Title,
				description = this.Description ?? string.Empty,
				isDraft = this.IsDraft,
				labels = this.Labels?.Select(name => new { name }) ?? [],
			}),
		};

		HttpResponseMessage? response = await this.SendAsync(requestMessage, canReadContent: false);
		await this.PrintErrorMessageAsync(response);
		if (this.IsSuccessResponse(response))
		{
			if (response.Content.Headers.ContentType?.MediaType == "application/json")
			{
				PullRequest? pr = await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.PullRequest, this.CancellationToken);
				if (pr is not null)
				{
					this.Out.WriteLine($"Pull request {pr.PullRequestId} created.");
					string prUrl = $"{pr.Repository.WebUrl}/pullrequest/{pr.PullRequestId}";
					this.Out.WriteLine(prUrl);
				}
			}
		}
	}
}
