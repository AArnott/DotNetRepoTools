// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using NuGet.Protocol;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class WorkItemCreateCommand : WorkItemCommandBase
{
	protected static readonly Argument<string> TypeArgument = new("type") { Description = "The type of work item to create.", Arity = ArgumentArity.ExactlyOne };

	protected static readonly Argument<string> TitleArgument = new("title") { Description = "The title for the work item.", Arity = ArgumentArity.ExactlyOne };

	protected static readonly Option<string> AreaPathOption = new("--area") { Description = "The area path for the work item." };

	protected static readonly Option<string> IterationPathOption = new("--iteration") { Description = "The iteration path for the work item." };

	protected static readonly Option<string> AssignedToOption = new("--assigned-to") { Description = "The identity to assign the work item to. This will typically be in the form user@domain.com." };

	public WorkItemCreateCommand()
	{
	}

	[SetsRequiredMembers]
	public WorkItemCreateCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.Type = parseResult.GetValue(TypeArgument)!;
		this.Title = parseResult.GetValue(TitleArgument)!;
		this.AreaPath = parseResult.GetValue(AreaPathOption)!;
		this.IterationPath = parseResult.GetValue(IterationPathOption);
		this.AssignedTo = parseResult.GetValue(AssignedToOption);
	}

	public required string Type { get; init; }

	public required string Title { get; init; }

	public string? AssignedTo { get; init; }

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
			AssignedToOption,
		};
		AddCommonOptions(command);

		command.SetAction((parseResult, cancellationToken) => new WorkItemCreateCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
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

		if (this.AssignedTo is not null)
		{
			patches.Add(new() { Path = "/fields/System.AssignedTo", Value = this.AssignedTo });
		}

		string workItemJson = this.ReadFromStandardIn("Provide a JSON object with properties that you want to set as fields on your work item.");
		JsonNode? workItem = JsonNode.Parse(workItemJson);
		if (workItem is null)
		{
			this.Error.WriteLine("The JSON you provided is not valid.");
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
				this.Out.WriteLine($"Created bug #{bugId.GetValue<int>()}");

				if (content?["_links"]?["html"]?["href"] is JsonValue url)
				{
					this.Out.WriteLine(url.GetValue<string>());
				}
			}
			else
			{
				this.Out.WriteLine(content?.ToString() ?? string.Empty);
			}
		}
		else
		{
			await this.PrintErrorMessageAsync(response);
		}
	}
}
