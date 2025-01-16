// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestCreateCommand : PullRequestCommandBase
{
	protected static readonly Option<string> TitleOption = new("--title", "The title of the pull request.") { IsRequired = true };
	protected static readonly Option<string> DescriptionOption = new("--description", "The description of the pull request. If not specified, input will be taken from STDIN.");
	protected static readonly Option<string> SourceRefNameOption = new("--source", "The name of the branch to merge from. This should not include the refs/heads/ prefix.") { IsRequired = true };
	protected static readonly Option<string> TargetRefNameOption = new("--target", "The name of the branch to merge into. This should not include the refs/heads/ prefix.") { IsRequired = true };
	protected static readonly Option<bool> IsDraftOption = new("--draft", "Whether the pull request is a draft.");
	protected static readonly Option<string[]> LabelsOption = new("--labels", "Labels to apply to the pull request.") { AllowMultipleArgumentsPerToken = true };

	public PullRequestCreateCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestCreateCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.Title = invocationContext.ParseResult.GetValueForOption(TitleOption)!;
		this.Description = invocationContext.ParseResult.GetValueForOption(DescriptionOption);
		this.SourceRefName = invocationContext.ParseResult.GetValueForOption(SourceRefNameOption)!;
		this.TargetRefName = invocationContext.ParseResult.GetValueForOption(TargetRefNameOption)!;
		this.IsDraft = invocationContext.ParseResult.GetValueForOption(IsDraftOption);
		this.Labels = invocationContext.ParseResult.GetValueForOption(LabelsOption);
	}

	public required string Title { get; init; }

	public string? Description { get; init; }

	public required string SourceRefName { get; init; }

	public required string TargetRefName { get; init; }

	public bool IsDraft { get; init; }

	public string[]? Labels { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("create", "Creates a new pull request")
		{
			TitleOption,
			DescriptionOption,
			SourceRefNameOption,
			TargetRefNameOption,
			IsDraftOption,
			LabelsOption,
		};
		AddCommonOptions(command);

		command.SetHandler(ctxt => new PullRequestCreateCommand(ctxt).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		HttpRequestMessage requestMessage = new(HttpMethod.Post, "?api-version=6.0")
		{
			Content = JsonContent.Create(new
			{
				sourceRefName = $"refs/heads/{this.SourceRefName}",
				targetRefName = $"refs/heads/{this.TargetRefName}",
				title = this.Title,
				description = this.Description ?? ReadFromStandardIn(),
				isDraft = this.IsDraft,
				labels = this.Labels?.Select(name => new { name }) ?? [],
			}),
		};

		HttpResponseMessage? response = await this.SendAsync(requestMessage, canReadContent: false);
		await this.PrintErrorMessageAsync(response);
		if (response is { IsSuccessStatusCode: true })
		{
			if (response.Content.Headers.ContentType?.MediaType == "application/json")
			{
				PullRequest? pr = await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.PullRequest, this.CancellationToken);
				if (pr is not null)
				{
					this.Console.WriteLine($"Pull request {pr.PullRequestId} created.");
					string prUrl = $"{pr.Repository.WebUrl}/pullrequest/{pr.PullRequestId}";
					this.Console.WriteLine(prUrl);
				}
			}
		}
	}
}
