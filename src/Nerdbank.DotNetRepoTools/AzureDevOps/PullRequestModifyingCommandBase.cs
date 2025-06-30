// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class PullRequestModifyingCommandBase : PullRequestCommandBase
{
	protected internal static readonly Argument<int?> PullRequestIdArgument = new("id") { Description = "The ID of the pull request." };

	protected static readonly Option<int?> PullRequestIdOption = new("--pull-request") { Description = "The ID of the pull request.", Required = true };

	protected PullRequestModifyingCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected PullRequestModifyingCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
		this.PullRequestId =
			parseResult.GetValue(PullRequestIdOption) ??
			parseResult.GetValue(PullRequestIdArgument) ??
			throw new InvalidOperationException("No Pull Request ID specified.");
	}

	public required int PullRequestId { get; init; }

	protected static new void AddCommonOptions(Command command) => AddCommonOptions(command, pullRequestIdAsArgument: false);

	protected static void AddCommonOptions(Command command, bool pullRequestIdAsArgument)
	{
		PullRequestCommandBase.AddCommonOptions(command);
		if (pullRequestIdAsArgument)
		{
			command.Arguments.Add(PullRequestIdArgument);
		}
		else
		{
			command.Options.Add(PullRequestIdOption);
		}
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}/{this.PullRequestId}/");
		return client;
	}
}
