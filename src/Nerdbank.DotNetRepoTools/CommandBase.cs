﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using Microsoft;

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// A base class for commands.
/// </summary>
public abstract class CommandBase : IDisposable
{
	/// <summary>
	/// The <c>--what-if</c> option that can be used to preview the effects of a command without actually executing it.
	/// </summary>
	protected static readonly Option<bool> WhatIfOption = new("--what-if", "Prints what would be done without actually doing it.");

	/// <summary>
	/// The --verbose option that should activate printing of spawned commands.
	/// </summary>
	protected static readonly Option<bool> VerboseOption = new("--verbose", "Prints the command lines of sub-processes spawned by the tool.");

	/// <summary>
	/// Initializes a new instance of the <see cref="CommandBase"/> class
	/// suitable for actually invoking the command.
	/// </summary>
	protected CommandBase()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="CommandBase"/> class
	/// suitable for actually invoking the command.
	/// </summary>
	/// <param name="invocationContext">The command line invocation context, from which to parse the arguments and get other interaction objects.</param>
	protected CommandBase(InvocationContext invocationContext)
	{
		Requires.NotNull(invocationContext);

		this.InvocationContext = invocationContext;
		this.Console = invocationContext.Console;
		this.CancellationToken = invocationContext.GetCancellationToken();

		this.WhatIf = invocationContext.ParseResult.GetValueForOption(WhatIfOption);
		this.Verbose = invocationContext.ParseResult.GetValueForOption(VerboseOption);
	}

	/// <summary>
	/// Gets or sets the exit code that should be returned from the tool.
	/// </summary>
	public int ExitCode { get; protected set; }

	/// <summary>
	/// Gets the cancellation token that applies to the command execution.
	/// </summary>
	public CancellationToken CancellationToken { get; init; }

	/// <summary>
	/// Gets the console to interact with during execution of the command.
	/// </summary>
	public IConsole Console { get; init; } = new TestConsole();

	/// <summary>
	/// Gets a value indicating whether to merely print the likely effects rather than apply them.
	/// </summary>
	public bool WhatIf { get; init; }

	/// <summary>
	/// Gets a value indicating whether to print the command lines of sub-processes spawned by the tool.
	/// </summary>
	public bool Verbose { get; init; }

	/// <summary>
	/// Gets the command line invocation context, when available.
	/// </summary>
	protected InvocationContext? InvocationContext { get; }

	/// <summary>
	/// Executes the command.
	/// </summary>
	/// <returns>A task that tracks command completion.</returns>
	public async Task ExecuteAsync()
	{
		try
		{
			await this.ExecuteCoreAsync();
		}
		catch
		{
			if (this.ExitCode == 0)
			{
				this.ExitCode = 1;
			}

			throw;
		}
		finally
		{
			if (this.InvocationContext is not null)
			{
				this.InvocationContext.ExitCode = this.ExitCode;
			}
		}
	}

	/// <summary>
	/// Executes the command and immediately disposes of it.
	/// </summary>
	/// <returns>A task that tracks command completion.</returns>
	public async Task ExecuteAndDisposeAsync()
	{
		await this.ExecuteAsync();
		this.Dispose();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		this.Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Finds the root of the git repository.
	/// </summary>
	/// <param name="startingPath">An optional starting path for the search that is expected to exist within a git repo. If <see langword="null" />, <see cref="Environment.CurrentDirectory"/> will be used.</param>
	/// <returns>The path to the root of the git repo, or <see langword="null"/> if none was found.</returns>
	internal static string? FindGitRepoRoot(string? startingPath = null)
	{
		// Look for a git repo
		for (string? subpath = startingPath ?? Environment.CurrentDirectory; subpath is not null; subpath = Path.GetDirectoryName(subpath))
		{
			string gitLocation = Path.Combine(subpath, ".git");
			if (File.Exists(gitLocation) || Directory.Exists(gitLocation))
			{
				// We found the root of a repo. It should be safe to change the file.
				return subpath;
			}
		}

		return null;
	}

	/// <summary>
	/// Adds common options to the specified command.
	/// </summary>
	/// <param name="command">The command to add options to.</param>
	protected static void AddCommonOptions(Command command)
	{
		command.AddOption(WhatIfOption);
		command.AddOption(VerboseOption);
	}

	/// <summary>
	/// Reads the standard input stream until it is closed and returns the content as a string.
	/// </summary>
	/// <param name="prompt">The prompt to indicate to the user what is expected.</param>
	/// <returns>The text from STDIN.</returns>
	protected string ReadFromStandardIn(string prompt)
	{
		if (!this.Console.IsInputRedirected)
		{
			this.Console.WriteLine(prompt);
			this.Console.WriteLine("Multiple lines are allowed.");
			this.Console.WriteLine(OperatingSystem.IsWindows() ? "Press Ctrl+Z and ENTER when done." : "Press Ctrl+D when done.");
			this.Console.WriteLine(string.Empty);
		}

		string? line;
		StringBuilder sb = new();
		while ((line = System.Console.ReadLine()) is not null)
		{
			sb.AppendLine(line);
		}

		return sb.ToString();
	}

	/// <summary>
	/// Implements the actual command implementation.
	/// </summary>
	/// <returns>A task that tracks command completion.</returns>
	protected abstract Task ExecuteCoreAsync();

	/// <summary>
	/// Disposes of managed and/or unmanaged resources owned by this instance.
	/// </summary>
	/// <param name="disposing"><see langword="true" /> if the object is being disposed; <see langword="false" /> if it is being finalized.</param>
	protected virtual void Dispose(bool disposing)
	{
	}
}
