// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace DotNetRepoTools.NuGet;

internal class TrimCommand : CommandBase
{
	private static readonly Argument<FileInfo> ProjectArgument = new Argument<FileInfo>("The path to the project file with the PackageReference items to trim.").ExistingOnly();

	public TrimCommand()
	{
	}

	public TrimCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Command command = new("trim", "Removes PackageReference items that are redundant because they are to packages that already appear as transitive dependencies.")
		{
		};
		command.SetHandler(ctxt => new TrimCommand(ctxt).ExecuteAsync());

		return command;
	}

	protected override Task ExecuteCoreAsync()
	{
		throw new NotImplementedException();
	}
}
