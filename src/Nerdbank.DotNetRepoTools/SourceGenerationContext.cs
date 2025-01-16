// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;
using Nerdbank.DotNetRepoTools.AzureDevOps;

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// JSON AOT source generation context.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PullRequest))]
[JsonSerializable(typeof(ErrorResponseWithMessage))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
