// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Evaluation;

internal class MSBuild
{
	internal ProjectCollection ProjectCollection { get; } = new();

	internal static ProjectItem? FindItem(Project project, string itemType, string include)
	{
		return project.GetItemsByEvaluatedInclude(include).FirstOrDefault(i => string.Equals(itemType, i.ItemType, StringComparison.OrdinalIgnoreCase));
	}

	internal Project EvaluateProjectFile(string projectFile)
	{
		return this.ProjectCollection.GetLoadedProjects(Path.GetFullPath(projectFile)).FirstOrDefault() ?? this.ProjectCollection.LoadProject(projectFile);
	}
}
