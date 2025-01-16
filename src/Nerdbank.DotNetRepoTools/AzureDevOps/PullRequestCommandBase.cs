// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class PullRequestCommandBase : AzureDevOpsCommandBase
{
	protected static readonly Option<string> PullRequestIdOption = new("--pull-request", "The ID of the pull request.") { IsRequired = true };

	protected PullRequestCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected PullRequestCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.PullRequestId = invocationContext.ParseResult.GetValueForOption(PullRequestIdOption)!;
	}

	public required string PullRequestId { get; init; }

	internal static Command Create()
	{
		Command command = new("pr", "Pull request commands")
		{
			PullRequestCommentCommand.CreateCommand(),
		};

		return command;
	}

	protected static new void AddCommonOptions(Command command)
	{
		AzureDevOpsCommandBase.AddCommonOptions(command);
		command.AddOption(PullRequestIdOption);
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri(client.BaseAddress!, $"pullRequests/{this.PullRequestId}/");
		return client;
	}
}
