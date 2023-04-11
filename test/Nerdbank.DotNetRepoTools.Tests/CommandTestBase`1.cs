// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft;

public abstract class CommandTestBase<TCommand> : TestBase
	where TCommand : CommandBase
{
	protected const string DirectoryPackagesPropsFileName = "Directory.Packages.props";

	protected CommandTestBase(ITestOutputHelper logger)
		: base(logger)
	{
	}

	protected TCommand? Command { get; set; }

	public override Task DisposeAsync()
	{
		if (this.Command is not null)
		{
			this.DumpConsole(this.Command.Console);
			this.Command.Dispose();
		}

		return base.DisposeAsync();
	}

	protected virtual async Task ExecuteCommandAsync()
	{
		Verify.Operation(this.Command is not null, $"Set {nameof(this.Command)} first.");

		this.MSBuild.SaveAll();
		await this.Command.ExecuteAndDisposeAsync();
		this.MSBuild.ReloadEverything();
	}
}
