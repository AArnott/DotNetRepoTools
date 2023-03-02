// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;

namespace DotNetRepoTools;

/// <summary>
/// A base class for commands.
/// </summary>
public abstract class CommandBase
{
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
		this.InvocationContext = invocationContext;
		this.Console = invocationContext.Console;
		this.CancellationToken = invocationContext.GetCancellationToken();
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
	/// Implements the actual command implementation.
	/// </summary>
	/// <returns>A task that tracks command completion.</returns>
	protected abstract Task ExecuteCoreAsync();
}
