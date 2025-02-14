// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using NuGet.Protocol;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class WorkItemCreateCommand : WorkItemCommandBase
{
	protected static readonly Argument<string> TypeArgument = new("type", "The type of work item to create.") { Arity = ArgumentArity.ExactlyOne };

	protected static readonly Argument<string> TitleArgument = new("title", "The title for the work item.") { Arity = ArgumentArity.ExactlyOne };

	protected static readonly Option<string> AreaPathOption = new("--area", "The area path for the work item.");

	protected static readonly Option<string> IterationPathOption = new("--iteration", "The iteration path for the work item.");

	public WorkItemCreateCommand()
	{
	}

	[SetsRequiredMembers]
	public WorkItemCreateCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.Type = invocationContext.ParseResult.GetValueForArgument(TypeArgument)!;
		this.Title = invocationContext.ParseResult.GetValueForArgument(TitleArgument)!;
		this.AreaPath = invocationContext.ParseResult.GetValueForOption(AreaPathOption)!;
		this.IterationPath = invocationContext.ParseResult.GetValueForOption(IterationPathOption);
	}

	public required string Type { get; init; }

	public required string Title { get; init; }

	public string? AreaPath { get; init; }

	public string? IterationPath { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("create", "Create a new work item.")
		{
			TypeArgument,
			TitleArgument,
			AreaPathOption,
			IterationPathOption,
		};
		AddCommonOptions(command);

		command.SetHandler(ctxt => new WorkItemCreateCommand(ctxt).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		List<JsonPatch> patches = [
			new() { Path = "/fields/System.Title", Value = this.Title },
			];

		if (this.AreaPath is not null)
		{
			patches.Add(new() { Path = "/fields/System.AreaPath", Value = this.AreaPath });
		}

		if (this.IterationPath is not null)
		{
			patches.Add(new() { Path = "/fields/System.IterationPath", Value = this.IterationPath });
		}

		string workItemJson = this.ReadFromStandardIn("Provide a JSON object with properties that you want to set as fields on your work item.");
		JsonNode? workItem = JsonNode.Parse(workItemJson);
		if (workItem is null)
		{
			this.Console.Error.WriteLine("The JSON you provided is not valid.");
			this.ExitCode = 1;
			return;
		}

		foreach ((string name, JsonNode? value) in workItem.AsObject())
		{
			patches.Add(new() { Path = $"/fields/{name}", Value = value! });
		}

		HttpRequestMessage request = new(HttpMethod.Post, $"${this.Type}?api-version=7.1")
		{
			Content = JsonContent.Create(
				patches,
				mediaType: new("application/json-patch+json")),
		};

		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: false);
		if (this.IsSuccessResponse(response))
		{
			JsonNode? content = await response.Content.ReadFromJsonAsync<JsonNode>();
			if (content?["id"] is JsonValue bugId)
			{
				this.Console.WriteLine($"Created bug #{bugId.GetValue<int>()}");

				if (content?["_links"]?["html"]?["href"] is JsonValue url)
				{
					this.Console.WriteLine(url.GetValue<string>());
				}
			}
			else
			{
				this.Console.WriteLine(content?.ToString() ?? string.Empty);
			}
		}
		else
		{
			await this.PrintErrorMessageAsync(response);
		}
	}
}
