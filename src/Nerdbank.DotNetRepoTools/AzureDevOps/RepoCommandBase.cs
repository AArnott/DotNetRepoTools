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
	protected RepoCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
		this.Repo = parseResult.GetValue(RepoOption)!;
	}

	public required string Repo { get; init; }

	protected static new void AddCommonOptions(Command command)
	{
		AzureDevOpsCommandBase.AddCommonOptions(command);
		command.Options.Add(RepoOption);
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}git/repositories/{this.Repo}/");
		return client;
	}
}
