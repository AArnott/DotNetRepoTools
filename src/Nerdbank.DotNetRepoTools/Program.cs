// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine.Parsing;
using Nerdbank.DotNetRepoTools.AzureDevOps;
using Nerdbank.DotNetRepoTools.Git;
using Nerdbank.DotNetRepoTools.NuGet;

namespace Nerdbank.DotNetRepoTools;

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
			GitCommand.CreateCommand(),
			AzureDevOpsCommandBase.CreateCommand(),
		};

		CommandLineConfiguration config = new(root);
		return config.InvokeAsync(args);
	}
}
