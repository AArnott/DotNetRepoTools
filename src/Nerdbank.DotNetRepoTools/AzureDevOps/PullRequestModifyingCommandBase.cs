// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class PullRequestModifyingCommandBase : PullRequestCommandBase
{
	protected internal static readonly Argument<int?> PullRequestIdArgument = new("id", "The ID of the pull request.") { Arity = ArgumentArity.ExactlyOne };

	protected static readonly Option<int?> PullRequestIdOption = new("--pull-request", "The ID of the pull request.") { IsRequired = true };

	protected PullRequestModifyingCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected PullRequestModifyingCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.PullRequestId =
			invocationContext.ParseResult.GetValueForOption(PullRequestIdOption) ??
			invocationContext.ParseResult.GetValueForArgument(PullRequestIdArgument) ??
			throw new InvalidOperationException("No Pull Request ID specified.");
	}

	public required int PullRequestId { get; init; }

	protected static new void AddCommonOptions(Command command) => AddCommonOptions(command, pullRequestIdAsArgument: false);

	protected static void AddCommonOptions(Command command, bool pullRequestIdAsArgument)
	{
		AzureDevOpsCommandBase.AddCommonOptions(command);
		if (pullRequestIdAsArgument)
		{
			command.AddArgument(PullRequestIdArgument);
		}
		else
		{
			command.AddOption(PullRequestIdOption);
		}
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}/{this.PullRequestId}/");
		return client;
	}
}
