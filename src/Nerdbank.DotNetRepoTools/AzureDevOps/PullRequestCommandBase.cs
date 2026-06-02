// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Base type for Azure DevOps pull request commands.
/// </summary>
public abstract class PullRequestCommandBase : RepoCommandBase
{
	/// <summary>
	/// Initializes a new instance of the <see cref="PullRequestCommandBase"/> class.
	/// </summary>
	protected PullRequestCommandBase()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="PullRequestCommandBase"/> class from parsed command-line data.
	/// </summary>
	/// <param name="parseResult">The parsed command-line result.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	[SetsRequiredMembers]
	protected PullRequestCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
	}

	internal static new Command CreateCommand()
	{
		Command command = new("pr", "Pull request commands")
		{
			PullRequestCommentCommand.CreateCommand(),
			PullRequestCreateCommand.CreateCommand(),
			PullRequestGetCommand.CreateCommand(),
			PullRequestSearchCommand.CreateCommand(),
			PullRequestUpdateCommand.CreateCommand(),
			PullRequestLinkCommand.CreateCommand(),
			PullRequestPropertyCommand.CreateCommand(),
			PullRequestReviewerCommandBase.CreateCommand(),
			PullRequestReviewerVoteCommand.CreateCommand(),
		};

		return command;
	}

	/// <summary>
	/// Creates the HTTP client used for pull request Azure DevOps requests.
	/// </summary>
	/// <returns>The HTTP client.</returns>
	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}/pullRequests");
		return client;
	}
}
