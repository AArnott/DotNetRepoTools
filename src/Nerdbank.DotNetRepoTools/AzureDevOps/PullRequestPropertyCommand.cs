// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestPropertyCommand : PullRequestModifyingCommandBase
{
	protected static readonly Argument<string> PathArgument = new("path") { Description = "The path to the property to set. This should always start with a '/' character.", Arity = ArgumentArity.ExactlyOne };
	protected static readonly Argument<string> ValueArgument = new("value") { Description = "The value of the property to set. Should be a JSON token. If not specified, STDIN will be used.", Arity = ArgumentArity.ZeroOrOne };

	public PullRequestPropertyCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestPropertyCommand(ParseResult parseResult, CancellationToken cancellationToken, string operation)
		: base(parseResult, cancellationToken)
	{
		this.Path = parseResult.GetValue(PathArgument)!;
		this.Value = parseResult.GetValue(ValueArgument);
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
		addCommand.SetAction((parseResult, cancellationToken) => new PullRequestPropertyCommand(parseResult, cancellationToken, "add").ExecuteAndDisposeAsync());

		Command removeCommand = new("remove", "Remove a property from a pull request.")
		{
			PathArgument,
		};
		AddCommonOptions(removeCommand);
		removeCommand.SetAction((parseResult, cancellationToken) => new PullRequestPropertyCommand(parseResult, cancellationToken, "remove").ExecuteAndDisposeAsync());

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
				new JsonArray(
					new JsonObject
					{
						["from"] = (string?)null,
						["op"] = this.Operation,
						["path"] = this.Path,
						["value"] = this.Value ?? (this.Operation == "add" ? this.ReadFromStandardIn($"Enter value for pull request property \"{this.Path}\".") : null),
					}),
				SourceGenerationContext.Default.JsonNode,
				mediaType: new("application/json-patch+json")),
		};

		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: true);
		if (response is { IsSuccessStatusCode: true })
		{
			this.Out.WriteLine("OK");
		}
	}
}
