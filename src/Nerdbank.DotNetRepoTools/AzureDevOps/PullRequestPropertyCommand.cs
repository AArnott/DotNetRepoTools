// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestPropertyCommand : PullRequestModifyingCommandBase
{
	protected static readonly Argument<string> PathArgument = new("path", "The path to the property to set. This should always start with a '/' character.") { Arity = ArgumentArity.ExactlyOne };
	protected static readonly Argument<string> ValueArgument = new("value", "The value of the property to set. Should be a JSON token. If not specified, STDIN will be used.") { Arity = ArgumentArity.ZeroOrOne };

	public PullRequestPropertyCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestPropertyCommand(InvocationContext invocationContext, string operation)
		: base(invocationContext)
	{
		this.Path = invocationContext.ParseResult.GetValueForArgument(PathArgument)!;
		this.Value = invocationContext.ParseResult.GetValueForArgument(ValueArgument);
		this.Operation = operation;
	}

	public required string Path { get; init; }

	public required string Operation { get; init; }

	public string? Value { get; init; }

	internal static new Command CreateCommand()
	{
		Command addCommand = new("add", "Set a property to a pull request.")
		{
			PathArgument,
			ValueArgument,
		};
		AddCommonOptions(addCommand);
		addCommand.SetHandler(ctxt => new PullRequestPropertyCommand(ctxt, "add").ExecuteAndDisposeAsync());

		Command removeCommand = new("remove", "Remove a property from a pull request.")
		{
			PathArgument,
		};
		AddCommonOptions(removeCommand);
		removeCommand.SetHandler(ctxt => new PullRequestPropertyCommand(ctxt, "remove").ExecuteAndDisposeAsync());

		Command command = new("property", "Operates on pull request properties.")
		{
			addCommand,
			removeCommand,
		};
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		HttpRequestMessage request = new(HttpMethod.Patch, "properties?api-version=7.1")
		{
			Content = JsonContent.Create(
				new[]
				{
					new
					{
						from = (string?)null,
						op = this.Operation,
						path = this.Path,
						value = this.Value ?? (this.Operation == "add" ? ReadFromStandardIn() : null),
					},
				},
				mediaType: new("application/json-patch+json")),
		};

		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: true);
		if (response is { IsSuccessStatusCode: true })
		{
			this.Console.WriteLine("OK");
		}
	}
}
