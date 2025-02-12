// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class PullRequestReviewerCommandBase : PullRequestModifyingCommandBase
{
	protected PullRequestReviewerCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected PullRequestReviewerCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	internal static new Command CreateCommand()
	{
		Command command = new("reviewer", "Reviewer changes and votes.")
		{
			PullRequestReviewerAddCommand.CreateCommand(),
		};

		return command;
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}/reviewers/");
		return client;
	}
}
