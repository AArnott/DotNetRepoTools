// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using DotNetRepoTools.NuGet;

namespace DotNetRepoTools;

/// <summary>
/// Executable entrypoint.
/// </summary>
internal static class Program
{
	private static Task<int> Main(string[] args)
	{
		RootCommand root = new($"A CLI tool with commands to help maintain .NET codebases.")
		{
			NuGetCommand.CreateCommand(),
		};
		root.Name = "repotools";
		return new CommandLineBuilder(root)
			.UseDefaults()
			.Build()
			.InvokeAsync(args);
	}
}
