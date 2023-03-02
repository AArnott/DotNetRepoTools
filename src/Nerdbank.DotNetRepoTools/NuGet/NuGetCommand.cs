// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;

namespace Nerdbank.DotNetRepoTools.NuGet;

/// <summary>
/// Aggregates the NuGet sub-commands.
/// </summary>
internal class NuGetCommand
{
	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Command nuget = new("nuget", "NuGet maintenance commands")
		{
			UpgradeCommand.CreateCommand(),
		};

		return nuget;
	}
}
