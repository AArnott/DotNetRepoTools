// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Execution;
using NuGet.Commands.Restore;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class ItemAdapter(ProjectItemInstance item) : IItem
{
	/// <inheritdoc/>
	public string Identity => item.EvaluatedInclude;

	/// <inheritdoc/>
	public string GetMetadata(string name) => item.GetMetadataValue(name).Trim();
}
