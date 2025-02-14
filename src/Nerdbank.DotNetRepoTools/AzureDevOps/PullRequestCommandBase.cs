// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class PullRequestCommandBase : RepoCommandBase
{
	protected PullRequestCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected PullRequestCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
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
			PullRequestPropertyCommand.CreateCommand(),
			PullRequestReviewerCommandBase.CreateCommand(),
			PullRequestReviewerVoteCommand.CreateCommand(),
		};

		return command;
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}/pullRequests");
		return client;
	}
}
