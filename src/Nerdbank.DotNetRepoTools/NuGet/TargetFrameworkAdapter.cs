// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Execution;
using NuGet.Commands.Restore;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class TargetFrameworkAdapter(ProjectInstance projectInstance) : ITargetFramework
{
	public IReadOnlyList<IItem> GetItems(string itemType) => [.. projectInstance.GetItems(itemType).Select(i => new ItemAdapter(i))];

	public string GetProperty(string propertyName) => projectInstance.GetPropertyValue(propertyName);
}
