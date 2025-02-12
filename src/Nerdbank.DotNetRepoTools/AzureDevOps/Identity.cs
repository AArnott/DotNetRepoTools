// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class Identity
{
	public required string Id { get; set; }

	public string? ProviderDisplayName { get; set; }
}
