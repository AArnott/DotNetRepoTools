// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine.Completions;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Nerdbank.DotNetRepoTools.Git;

internal abstract class GitCommandBase : CommandBase
{
	protected GitCommandBase()
	{
	}

	protected GitCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	protected static IEnumerable<string> GitRefCompletions(CompletionContext context)
	{
		const string LocalBranchPrefix = "refs/heads/";
		const string RemoteBranchPrefix = "refs/remotes/";

		using CancellationTokenSource cts = new(1000);

		foreach (string branch in QueryGit("git branch -a --format %(refname)", cts.Token))
		{
			string? simpleName = null;
			if (branch.StartsWith(LocalBranchPrefix))
			{
				simpleName = branch.Substring(LocalBranchPrefix.Length);
			}
			else if (branch.StartsWith(RemoteBranchPrefix))
			{
				simpleName = branch.Substring(RemoteBranchPrefix.Length);
			}

			if (simpleName?.StartsWith(context.WordToComplete, StringComparison.OrdinalIgnoreCase) is true)
			{
				yield return simpleName;
			}
		}
	}

	protected static IEnumerable<string> QueryGit(string arguments, CancellationToken cancellationToken)
	{
		ProcessStartInfo psi = new("git", arguments)
		{
			RedirectStandardOutput = true,
		};
		using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn git.");
		using (cancellationToken.Register(() => process.Kill()))
		{
			string? line;
			while ((line = process.StandardOutput.ReadLine()) is not null)
			{
				yield return line;
			}

			process.WaitForExit(500);
			process.Kill();
		}
	}

	protected static async IAsyncEnumerable<string> QueryGitAsync(string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ProcessStartInfo psi = new("git", arguments)
		{
			RedirectStandardOutput = true,
		};
		Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn git.");
		using (cancellationToken.Register(() => process.Kill()))
		{
			string? line;
			while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
			{
				yield return line;
			}

			await process.WaitForExitAsync();
		}
	}

	protected static async Task<int> ExecGitAsync(string arguments, CancellationToken cancellationToken)
	{
		ProcessStartInfo psi = new("git", arguments);
		Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn git.");
		using (cancellationToken.Register(() => process.Kill()))
		{
			await process.WaitForExitAsync();
			return process.ExitCode;
		}
	}
}
