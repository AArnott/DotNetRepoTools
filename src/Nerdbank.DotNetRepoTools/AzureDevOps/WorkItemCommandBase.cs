// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class WorkItemCommandBase : AzureDevOpsCommandBase
{
	protected WorkItemCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected WorkItemCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
	}

	internal static new Command CreateCommand()
	{
		return new Command("workitem", "Interact with work items in Azure DevOps.")
		{
			WorkItemCreateCommand.CreateCommand(),
		};
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}wit/workitems/");
		return client;
	}
}
