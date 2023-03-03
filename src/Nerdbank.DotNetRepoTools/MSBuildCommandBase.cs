// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine.Invocation;

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// A base class for commands that interact with MSBuild.
/// </summary>
public abstract class MSBuildCommandBase : CommandBase
{
	private bool msbuildOwned = true;
	private MSBuild msbuild = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="MSBuildCommandBase"/> class.
	/// </summary>
	protected MSBuildCommandBase()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="MSBuildCommandBase"/> class.
	/// </summary>
	/// <inheritdoc cref="CommandBase(InvocationContext)"/>
	protected MSBuildCommandBase(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	/// <summary>
	/// Gets the msbuild helper to use.
	/// </summary>
	public MSBuild MSBuild
	{
		get => this.msbuild;
		internal set
		{
			if (this.msbuild != value)
			{
				this.msbuild = value;
				this.msbuildOwned = false;
			}
		}
	}

	/// <inheritdoc/>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (this.msbuildOwned)
			{
				this.msbuild.Dispose();
			}
		}

		base.Dispose(disposing);
	}
}
