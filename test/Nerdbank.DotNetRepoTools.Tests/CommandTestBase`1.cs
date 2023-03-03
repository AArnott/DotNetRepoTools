// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public abstract class CommandTestBase<TCommand> : TestBase
	where TCommand : CommandBase
{
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
		}

		return base.DisposeAsync();
	}
}
