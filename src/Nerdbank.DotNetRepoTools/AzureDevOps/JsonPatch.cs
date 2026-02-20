// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Nodes;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class JsonPatch
{
	public JsonPatchOperation Op { get; set; } = JsonPatchOperation.add;

	public required string Path { get; set; } = string.Empty;

	public string? From { get; set; }

	public JsonNode? Value { get; set; }
}
