// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using NuGet.Common;
using NuGet.Credentials;

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
		DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);
		Command nuget = new("nuget", "NuGet maintenance commands")
		{
			ReconcileVersionsCommand.CreateCommand(),
			UpgradeCommand.CreateCommand(),
			TrimCommand.CreateCommand(),
			ManagePackageVersionsCentrally.CreateCommand(),
		};

		return nuget;
	}
}
