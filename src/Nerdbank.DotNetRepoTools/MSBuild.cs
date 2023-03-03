﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Nerdbank.DotNetRepoTools.NuGet;

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// A common set of MSBuild functionality.
/// </summary>
public class MSBuild : IDisposable
{
	/// <summary>
	/// Gets or sets a value indicating whether dirty projects will be saved on disposal.
	/// </summary>
	public bool SaveOnDisposal { get; set; } = true;

	internal ProjectCollection ProjectCollection { get; } = new();

	/// <summary>
	/// Finds an item in the project.
	/// </summary>
	/// <param name="project">The project to search.</param>
	/// <param name="itemType">The item type.</param>
	/// <param name="include">The evaluated include value.</param>
	/// <returns>The item, if found.</returns>
	public static ProjectItem? FindItem(Project project, string itemType, string include)
	{
		return project.GetItemsByEvaluatedInclude(include).FirstOrDefault(i => string.Equals(itemType, i.ItemType, StringComparison.OrdinalIgnoreCase));
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (this.SaveOnDisposal)
		{
			foreach (Project project in this.ProjectCollection.LoadedProjects)
			{
				project.Save();
			}
		}

		this.ProjectCollection.Dispose();
	}

	/// <summary>
	/// Gets a project evaluation for a given path.
	/// </summary>
	/// <param name="projectFile">The project's full path.</param>
	/// <returns>The project.</returns>
	public Project GetProject(string projectFile)
	{
		return this.ProjectCollection.GetLoadedProjects(Path.GetFullPath(projectFile)).FirstOrDefault() ?? this.ProjectCollection.LoadProject(projectFile);
	}

	/// <summary>
	/// Creates an in-memory project.
	/// </summary>
	/// <param name="projectPath">The path that the in-memory project should pretend to be at.</param>
	/// <param name="xml">The XML to read into the project. If not specified, the project will be empty.</param>
	/// <returns>The in-memory project.</returns>
	public Project SynthesizeVolatileProject(string projectPath, Stream? xml = null)
	{
		// We don't actually persist the file, but we create it in memory, which is as good as, without the persistent file to delete later.
		ProjectRootElement pre = ProjectRootElement.Create(this.ProjectCollection);
		if (xml is not null)
		{
			using XmlReader xmlReader = XmlReader.Create(xml);
			pre.ReloadFrom(xmlReader, throwIfUnsavedChanges: false);
		}

		return new Project(pre, ImmutableDictionary<string, string>.Empty, toolsVersion: null, this.ProjectCollection)
		{
			FullPath = projectPath,
		};
	}

	/// <summary>
	/// Synthesizes a project in memory, under some path.
	/// </summary>
	/// <param name="baseDirectory">The path under which the in-memory project should exist.</param>
	/// <returns>The synthesized project.</returns>
	internal Project CreateSandboxProject(string baseDirectory) => this.SynthesizeVolatileProject(Path.Combine(baseDirectory, "repotools_" + Path.GetRandomFileName(), "repotools.csproj"));

	/// <summary>
	/// Fills a project with a <c>PackageReference</c> for every <c>PackageVersion</c> item found in its evaluation.
	/// </summary>
	/// <param name="project">The project to populate.</param>
	internal void FillWithPackageReferences(Project project)
	{
		foreach (ProjectItem packageVersion in project.GetItems(NuGetHelper.PackageVersionItemType))
		{
			project.AddItemFast("PackageReference", packageVersion.EvaluatedInclude);
		}
	}
}
