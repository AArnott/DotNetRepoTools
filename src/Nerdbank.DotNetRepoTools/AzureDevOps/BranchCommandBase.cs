// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal abstract class BranchCommandBase : RepoCommandBase
{
	protected BranchCommandBase()
	{
	}

	[SetsRequiredMembers]
	protected BranchCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
	}

	internal static new Command CreateCommand()
	{
		Command command = new("branch", "Branch commands")
		{
			BranchFavoriteCommand.CreateCommand(),
			BranchUnfavoriteCommand.CreateCommand(),
		};

		return command;
	}
}
