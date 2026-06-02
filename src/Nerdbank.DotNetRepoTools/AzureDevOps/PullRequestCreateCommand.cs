// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Creates a new Azure DevOps pull request.
/// </summary>
public class PullRequestCreateCommand : PullRequestCommandBase
{
	private const string JsonOutputFormat = "json";

	private static readonly Option<string> TitleOption = new("--title") { Description = "The title of the pull request.", Required = true };
	private static readonly Option<string> DescriptionOption = new("--description") { Description = "The description of the pull request. If an argument for this option is not specified on the command line, it will be pulled in from STDIN.", Arity = ArgumentArity.ZeroOrOne };
	private static readonly Option<string> SourceRefNameOption = new("--source") { Description = "The name of the branch to merge from. This may be a short branch name or a fully-qualified ref such as refs/heads/main. Defaults to the current git branch when omitted." };
	private static readonly Option<string> TargetRefNameOption = new("--target") { Description = "The name of the branch to merge into. This may be a short branch name or a fully-qualified ref such as refs/heads/main. Defaults to the repository's Azure DevOps default branch when omitted." };
	private static readonly Option<bool> IsDraftOption = new("--draft") { Description = "Whether the pull request is a draft." };
	private static readonly Option<string[]> LabelsOption = new("--labels") { Description = "Labels to apply to the pull request.", AllowMultipleArgumentsPerToken = true };
	private static readonly Option<OutputFormat> FormatOption = new("--format") { Description = "The output format to write." };

	/// <summary>
	/// Initializes a new instance of the <see cref="PullRequestCreateCommand"/> class.
	/// </summary>
	public PullRequestCreateCommand()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="PullRequestCreateCommand"/> class from parsed command-line data.
	/// </summary>
	/// <param name="parseResult">The parsed command-line result.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	[SetsRequiredMembers]
	public PullRequestCreateCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.Title = parseResult.GetValue(TitleOption)!;
		this.Description = parseResult.GetValue(DescriptionOption);
		this.SourceRefName = parseResult.GetValue(SourceRefNameOption);
		this.TargetRefName = parseResult.GetValue(TargetRefNameOption);
		this.IsDraft = parseResult.GetValue(IsDraftOption);
		this.Labels = parseResult.GetValue(LabelsOption);
		this.Format = parseResult.GetValue(FormatOption);

		this.GetDescriptionFromStdIn = this.Description is null && parseResult.Tokens.Any(t => DescriptionOption.HasAlias(t.Value));
	}

	/// <summary>
	/// Gets the pull request title.
	/// </summary>
	public required string Title { get; init; }

	/// <summary>
	/// Gets or sets the pull request description.
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Gets a value indicating whether the description should be read from standard input.
	/// </summary>
	public bool GetDescriptionFromStdIn { get; init; }

	/// <summary>
	/// Gets the source branch name.
	/// This may be a short branch name or a fully-qualified ref such as <c>refs/heads/main</c>.
	/// </summary>
	public string? SourceRefName { get; init; }

	/// <summary>
	/// Gets the target branch name.
	/// This may be a short branch name or a fully-qualified ref such as <c>refs/heads/main</c>.
	/// </summary>
	public string? TargetRefName { get; init; }

	/// <summary>
	/// Gets a value indicating whether the pull request is a draft.
	/// </summary>
	public bool IsDraft { get; init; }

	/// <summary>
	/// Gets the labels to apply to the pull request.
	/// </summary>
	public string[]? Labels { get; init; }

	/// <summary>
	/// Gets the output format.
	/// </summary>
	public OutputFormat Format { get; init; }

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
			FormatOption,
		};
		AddCommonOptions(command);

		command.SetAction((parseResult, cancellationToken) => new PullRequestCreateCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	/// <summary>
	/// Executes the pull request creation.
	/// </summary>
	/// <returns>A task that completes when the operation finishes.</returns>
	protected override async Task ExecuteCoreAsync()
	{
		string? sourceRefName = this.SourceRefName ?? CommandBase.TryGetCurrentGitBranch();
		if (string.IsNullOrEmpty(sourceRefName))
		{
			this.Error.WriteLine("Specify --source or run this command from a git repository with a checked out branch.");
			this.ExitCode = 1;
			return;
		}

		string? targetRefName = await this.GetTargetRefNameAsync();
		if (string.IsNullOrEmpty(targetRefName))
		{
			if (this.ExitCode == 0)
			{
				this.Error.WriteLine("Specify --target or configure a default branch for the Azure DevOps repository.");
				this.ExitCode = 1;
			}

			return;
		}

		if (this.GetDescriptionFromStdIn)
		{
			this.Description = this.ReadFromStandardIn("Enter description for pull request.");
		}

		JsonArray labelsArray = new();
		if (this.Labels is not null)
		{
			foreach (string name in this.Labels)
			{
				labelsArray.Add((JsonNode)new JsonObject { ["name"] = name });
			}
		}

		HttpRequestMessage requestMessage = new(HttpMethod.Post, "?api-version=6.0")
		{
			Content = JsonContent.Create(
				new JsonObject
				{
					["sourceRefName"] = PrefixRef("refs/heads/", sourceRefName),
					["targetRefName"] = PrefixRef("refs/heads/", targetRefName),
					["title"] = this.Title,
					["description"] = this.Description ?? string.Empty,
					["isDraft"] = this.IsDraft,
					["labels"] = labelsArray,
				},
				SourceGenerationContext.Default.JsonNode),
		};

		HttpResponseMessage? response = await this.SendAsync(requestMessage, canReadContent: false);
		await this.PrintErrorMessageAsync(response);
		if (this.IsSuccessResponse(response))
		{
			if (response.Content.Headers.ContentType?.MediaType == "application/json")
			{
				if (this.Format == OutputFormat.Json)
				{
					this.Out.WriteLine(await response.Content.ReadAsStringAsync(this.CancellationToken));
				}
				else
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

	private async Task<string?> GetTargetRefNameAsync()
	{
		if (!string.IsNullOrEmpty(this.TargetRefName))
		{
			return this.TargetRefName;
		}

		GitRepository? repository = await this.GetRepositoryAsync();
		return repository?.DefaultBranch;
	}
}
