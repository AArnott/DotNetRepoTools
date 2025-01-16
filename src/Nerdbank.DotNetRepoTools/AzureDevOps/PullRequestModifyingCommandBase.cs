// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class PullRequestModifyingCommandBase : PullRequestCommandBase
{
	protected static readonly Option<string> PullRequestIdOption = new("--pull-request", "The ID of the pull request.") { IsRequired = true };

	protected PullRequestModifyingCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected PullRequestModifyingCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.PullRequestId = invocationContext.ParseResult.GetValueForOption(PullRequestIdOption)!;
	}

	public required string PullRequestId { get; init; }

	protected static new void AddCommonOptions(Command command)
	{
		AzureDevOpsCommandBase.AddCommonOptions(command);
		command.AddOption(PullRequestIdOption);
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}/{this.PullRequestId}/");
		return client;
	}
}
