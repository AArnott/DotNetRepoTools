// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// A base class for commands that interact with MSBuild.
/// </summary>
public abstract class MSBuildCommandBase : CommandBase
{
	private protected const string DirectoryPackagesPropsFileName = "Directory.Packages.props";

	private bool msbuildOwned = true;
	private MSBuild msbuild = new();

	static MSBuildCommandBase()
	{
		MSBuild.MSBuildLocator.EnsureLoaded();
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="MSBuildCommandBase"/> class.
	/// </summary>
	protected MSBuildCommandBase()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="MSBuildCommandBase"/> class.
	/// </summary>
	/// <inheritdoc cref="CommandBase(ParseResult, CancellationToken)"/>
	protected MSBuildCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
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
