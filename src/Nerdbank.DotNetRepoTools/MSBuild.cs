// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Enumeration;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Nerdbank.DotNetRepoTools.NuGet;
using Newtonsoft.Json.Linq;

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// A common set of MSBuild functionality.
/// </summary>
public class MSBuild : IDisposable
{
	private readonly Dictionary<string, bool> filesThatMayBeChanged = new(StringComparer.OrdinalIgnoreCase);
	private string? repoRoot;

	static MSBuild()
	{
		MSBuildLocator.EnsureLoaded();
	}

	/// <summary>
	/// Gets or sets the path to the repo root directory or otherwise the directory above which no file changes should ever be persisted.
	/// This will always end with a directory separator character.
	/// </summary>
	public string? RepoRoot
	{
		get => this.repoRoot;
		set => this.repoRoot = EnsureTrailingSlash(value);
	}

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
		this.SaveAll();
		this.ProjectCollection.Dispose();
	}

	/// <summary>
	/// Saves all changed project files and imports to disk.
	/// </summary>
	public void SaveAll()
	{
		foreach (ProjectRootElement pre in this.EnumerateLoadedXml())
		{
			if (pre.HasUnsavedChanges && !string.Equals(Path.GetFileName(pre.FullPath), "repotools.csproj", StringComparison.OrdinalIgnoreCase))
			{
				pre.Save();
			}
		}
	}

	/// <summary>
	/// Gets a project evaluation for a given path.
	/// </summary>
	/// <param name="projectFile">The project's full path.</param>
	/// <param name="projectLoadSettings">Settings to apply when loading the project, if it has not already been loaded.</param>
	/// <returns>The project.</returns>
	public Project GetProject(string projectFile, ProjectLoadSettings projectLoadSettings = ProjectLoadSettings.Default)
	{
		return this.ProjectCollection.GetLoadedProjects(Path.GetFullPath(projectFile)).FirstOrDefault() ?? new Project(projectFile, null, null, this.ProjectCollection, projectLoadSettings);
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
		else
		{
			pre.Sdk = "Microsoft.NET.Sdk";
		}

		pre.FullPath = projectPath;
		return new Project(pre, ImmutableDictionary<string, string>.Empty, toolsVersion: null, this.ProjectCollection)
		{
		};
	}

	/// <summary>
	/// Reloads all project files and imports from disk.
	/// </summary>
	public void ReloadEverything()
	{
		string? originatingLocation = this.ProjectCollection.LoadedProjects.FirstOrDefault()?.FullPath;
		if (originatingLocation is null)
		{
			return;
		}

		foreach (ProjectRootElement pre in this.EnumerateLoadedXml())
		{
			if (File.Exists(pre.FullPath) && this.CanChangeFile(pre.FullPath, originatingLocation))
			{
				pre.Reload(throwIfUnsavedChanges: false);
			}
		}
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

	internal bool CanChangeFile(string path, string originatingLocation)
	{
		if (this.filesThatMayBeChanged.TryGetValue(path, out bool cachedResult))
		{
			return cachedResult;
		}

		if (this.RepoRoot is not null && path.StartsWith(this.RepoRoot, StringComparison.OrdinalIgnoreCase))
		{
			cachedResult = true;
		}

		// If we're in a git repo, it should be safe to change the file.
		cachedResult |= CommandBase.FindGitRepoRoot(Path.GetDirectoryName(path)) is not null;

		if (!cachedResult)
		{
			// If the file to be changed is in the same directory as the originating location,
			// or a direct ancestor, or a descendant, then we're safe.
			// Basically, don't change files that are in cousin directories,
			// lest we change something under Program Files.
			string? changedDirectory = Path.GetDirectoryName(path);
			string? originatingDirectory = Directory.Exists(originatingLocation) ? originatingLocation : Path.GetDirectoryName(originatingLocation);
			if (changedDirectory is not null && originatingDirectory is not null)
			{
				originatingDirectory = EnsureTrailingSlash(originatingDirectory);
				cachedResult |= path.StartsWith(originatingDirectory, StringComparison.OrdinalIgnoreCase) || originatingDirectory.StartsWith(changedDirectory, StringComparison.OrdinalIgnoreCase);
			}
		}

		this.filesThatMayBeChanged.Add(path, cachedResult);
		return cachedResult;
	}

	[return: NotNullIfNotNull(nameof(path))]
	private static string? EnsureTrailingSlash(string? path) => path is null || path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

	private IEnumerable<ProjectRootElement> EnumerateLoadedXml()
	{
		foreach (Project project in this.ProjectCollection.LoadedProjects)
		{
			yield return project.Xml;
			foreach (ResolvedImport resolvedImport in project.Imports)
			{
				if (resolvedImport.ImportedProject is not null)
				{
					yield return resolvedImport.ImportedProject;
				}
			}
		}
	}

	/// <summary>
	/// Finds and uses the MSBuild that is installed on the machine.
	/// </summary>
	public static class MSBuildLocator
	{
		static MSBuildLocator()
		{
			Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
		}

		/// <summary>
		/// Ensures that the MSBuild Locator has been run.
		/// </summary>
		public static void EnsureLoaded()
		{
		}
	}
}
