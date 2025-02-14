// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class RepoCommandBase : AzureDevOpsCommandBase
{
	protected static readonly OptionOrEnvVar RepoOption = new("--repo", "BUILD_REPOSITORY_NAME", isRequired: true, "The name of the repo.");

	protected RepoCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected RepoCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.Repo = invocationContext.ParseResult.GetValueForOption(RepoOption)!;
	}

	public required string Repo { get; init; }

	protected static new void AddCommonOptions(Command command)
	{
		AzureDevOpsCommandBase.AddCommonOptions(command);
		command.AddOption(RepoOption);
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}git/repositories/{this.Repo}/");
		return client;
	}
}
