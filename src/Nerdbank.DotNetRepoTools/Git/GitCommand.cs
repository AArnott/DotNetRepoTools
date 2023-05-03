// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;

namespace Nerdbank.DotNetRepoTools.Git;

/// <summary>
/// Aggregates the git sub-commands.
/// </summary>
internal class GitCommand
{
	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Command git = new("git", "Git repo maintenance workflows")
		{
			TrimCommand.CreateCommand(),
		};

		return git;
	}
}
