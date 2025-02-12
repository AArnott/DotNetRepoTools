// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class AzDOArray<T>
{
	public int Count { get; set; }

	public required T[] Value { get; set; }
}
