﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;

namespace Nerdbank.DotNetRepoTools.Git;

internal class TrimCommand : GitCommandBase
{
	public TrimCommand()
	{
	}

	public TrimCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	/// <summary>
	/// Gets the ref of the object that is the ultimate object of any branch.
	/// Once a branch has merged into this ref, it can be deleted.
	/// </summary>
	public required string MergedInto { get; init; }

	/// <summary>
	/// Gets a value indicating whether to merely identify branches that have been merged into <see cref="MergedInto"/>
	/// rather than delete them.
	/// </summary>
	public bool WhatIf { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<string> mergedIntoArg = new("mergeTarget", "Branches become trimmable after they have been merged into this ref. Typically origin/main or similar.")
		{
			HelpName = "mergeTarget",
		};
		mergedIntoArg.AddCompletions(GitRefCompletions);
		Command command = new("trim", "Removes local branches that have already been merged into some target ref. Squashed branches can sometimes also be detected.")
		{
			mergedIntoArg,
			WhatIfOption,
			VerboseOption,
		};
		command.SetHandler(ctxt => new TrimCommand(ctxt)
		{
			MergedInto = ctxt.ParseResult.GetValueForArgument(mergedIntoArg),
			WhatIf = ctxt.ParseResult.GetValueForOption(WhatIfOption),
			Verbose = ctxt.ParseResult.GetValueForOption(VerboseOption),
		}.ExecuteAndDisposeAsync());

		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		const string LocalBranchPrefix = "refs/heads/";

		List<string> branchesToDelete = [];
		await foreach (string branch in this.QueryGitAsync($"branch --merged {this.MergedInto} --format %(refname)", this.CancellationToken))
		{
			// Skip output such as "(HEAD detached at origin/main)"
			if (branch.StartsWith(LocalBranchPrefix))
			{
				branchesToDelete.Add(branch.Substring(LocalBranchPrefix.Length));
			}
		}

		await foreach (string branch in this.QueryGitAsync($"branch --no-merged {this.MergedInto} --format %(refname)", this.CancellationToken))
		{
			// Skip output such as "(HEAD detached at origin/main)"
			if (branch.StartsWith(LocalBranchPrefix))
			{
				bool novelCommitsFound = false;
				await foreach (string line in this.QueryGitAsync($"cherry {this.MergedInto} {branch}", this.CancellationToken))
				{
					if (line.StartsWith("+"))
					{
						novelCommitsFound = true;
						break;
					}
				}

				if (!novelCommitsFound)
				{
					branchesToDelete.Add(branch.Substring(LocalBranchPrefix.Length));
				}
			}
		}

		branchesToDelete.Sort(StringComparer.OrdinalIgnoreCase);
		if (this.WhatIf)
		{
			this.Console.WriteLine("The following branches are trimmable:");
			foreach (string branch in branchesToDelete)
			{
				this.Console.WriteLine($"  {branch}");
			}
		}
		else
		{
			StringBuilder branchListAsString = new();
			const string DeleteBranchCommandPrefix = "branch -D ";
			while (branchesToDelete.Count > 0)
			{
				// Greedily delete many branches at once provided the command line doesn't get so long that it tends to fail.
				branchListAsString.Clear();
				while (branchesToDelete.Count > 0 && DeleteBranchCommandPrefix.Length + branchListAsString.Length + 1 + branchesToDelete[0].Length < 8192)
				{
					branchListAsString.Append(' ');
					branchListAsString.Append(branchesToDelete[0]);
					branchesToDelete.RemoveAt(0);
				}

				await this.ExecGitAsync($"{DeleteBranchCommandPrefix}{branchListAsString}", this.CancellationToken);
			}
		}
	}
}
