﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace Nerdbank.DotNetRepoTools.NuGet;

/// <summary>
/// Migrates a repo to use centralized package versions.
/// </summary>
public class ManagePackageVersionsCentrally : MSBuildCommandBase
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ManagePackageVersionsCentrally"/> class.
	/// </summary>
	public ManagePackageVersionsCentrally()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ManagePackageVersionsCentrally"/> class.
	/// </summary>
	/// <inheritdoc cref="CommandBase(ParseResult, CancellationToken)"/>
	public ManagePackageVersionsCentrally(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
	}

	/// <summary>
	/// Gets the path to the project file or repo to upgrade.
	/// </summary>
	public required string Path { get; init; }

	/// <summary>
	/// Gets the path to the Directory.Packages.props file.
	/// </summary>
	public required string DirectoryPackagesPropsPath { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Option<FileSystemInfo> pathOption = new Option<FileSystemInfo>("--path") { DefaultValueFactory = _ => new DirectoryInfo(Environment.CurrentDirectory), Description = "The path to the project or repo to upgrade." }.AcceptExistingOnly();
		Option<FileSystemInfo> repoBaseOption = new Option<FileSystemInfo>("--repo-root") { Description = "The path to the directory where the Directory.Packages.props is to be created. Defaults to the git repo root above the location specified by --path." }.AcceptExistingOnly();

		Command command = new("ManagePackageVersionsCentrally", "Migrates a repo to use centralized package versions.")
		{
			pathOption,
			repoBaseOption,
		};
		command.SetAction((parseResult, cancellationToken) =>
		{
			string path = parseResult.GetValue(pathOption)?.FullName ?? Environment.CurrentDirectory;
			string? repoRoot = parseResult.GetValue(repoBaseOption)?.FullName ?? FindGitRepoRoot(path);
			if (repoRoot is null)
			{
				throw new Exception("No git repo found and --repo-root was not specified.");
			}

			return new ManagePackageVersionsCentrally(parseResult, cancellationToken)
			{
				Path = path,
				DirectoryPackagesPropsPath = System.IO.Path.Combine(repoRoot, DirectoryPackagesPropsFileName),
			}.ExecuteAndDisposeAsync();
		});

		return command;
	}

	/// <inheritdoc/>
	protected override Task ExecuteCoreAsync()
	{
		// Create the Directory.Packages.props file if it doesn't already exist.
		Project directoryPackagesProps = File.Exists(this.DirectoryPackagesPropsPath)
			? this.MSBuild.GetProject(this.DirectoryPackagesPropsPath)
			: this.CreateDirectoryPackagesProps();

		// Enumerate each project at or under the provided path, and replace items in each one.
		if (File.Exists(this.Path))
		{
			this.ProcessProjectFile(this.Path, directoryPackagesProps);
		}
		else if (Directory.Exists(this.Path))
		{
			foreach (string csprojFile in Directory.GetFiles(this.Path, "*.csproj", SearchOption.AllDirectories))
			{
				this.ProcessProjectFile(csprojFile, directoryPackagesProps);
			}
		}
		else
		{
			this.Error.WriteLine($"The path \"{this.Path}\" does not exist.");
			this.ExitCode = 1;
		}

		return Task.CompletedTask;
	}

	private Project CreateDirectoryPackagesProps()
	{
		Project project = new Project(this.MSBuild.ProjectCollection)
		{
			FullPath = this.DirectoryPackagesPropsPath,
			Xml = { ToolsVersion = null },
		};
		project.SetProperty("ManagePackageVersionsCentrally", "true");

		return project;
	}

	private void ProcessProjectFile(string projectPath, Project directoryPackagesProps)
	{
		string relativeProjectPath = System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(this.DirectoryPackagesPropsPath)!, projectPath);
		Project project;
		try
		{
			project = this.MSBuild.GetProject(projectPath, ProjectLoadSettings.IgnoreMissingImports | ProjectLoadSettings.IgnoreInvalidImports);
		}
		catch (InvalidProjectFileException ex)
		{
			this.Error.WriteLine($"Error on \"{relativeProjectPath}\": {ex.Message}");
			this.ExitCode = 2;
			return;
		}

		if (project.Imports.Count == 0)
		{
			this.Error.WriteLine($"Project \"{relativeProjectPath}\" does not import anything. Did you run restore? That's important to do first.");
		}

		directoryPackagesProps.ReevaluateIfNecessary();
		HashSet<ProjectRootElement> changedFiles = new();
		int versionOverrides = 0;
		foreach (ProjectItem packageReference in project.GetItemsIgnoringCondition("PackageReference"))
		{
			// Only inspect items within the root path.
			if (!packageReference.Xml.ContainingProject.FullPath.StartsWith(this.Path))
			{
				continue;
			}

			ProjectMetadata? packageReferenceVersionMetadata = packageReference.GetMetadata("Version");
			if (packageReferenceVersionMetadata is not null)
			{
				// If the version is already in the Directory.Packages.props file, remove it from the project.
				ProjectItem? packageVersion = MSBuild.FindItem(directoryPackagesProps, "PackageVersion", packageReference.EvaluatedInclude);
				if (packageVersion is null)
				{
					packageVersion = directoryPackagesProps.AddItem("PackageVersion", ProjectCollection.Escape(packageReference.EvaluatedInclude)).Single();
					packageVersion.Xml.AddMetadata("Version", packageReferenceVersionMetadata.UnevaluatedValue, expressAsAttribute: true);
				}
				else
				{
					if (!string.Equals(packageVersion.GetMetadataValue("Version"), packageReferenceVersionMetadata.EvaluatedValue, StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(packageVersion.GetMetadata("Version")?.UnevaluatedValue, packageReferenceVersionMetadata.UnevaluatedValue, StringComparison.OrdinalIgnoreCase))
					{
						// A version is already specified, but does not match what this particular project wanted.
						// Change to VersionOverride metadata.
						packageReference.Xml.AddMetadata("VersionOverride", packageReferenceVersionMetadata.UnevaluatedValue, expressAsAttribute: true);
						versionOverrides++;
					}
				}

				changedFiles.Add(packageReferenceVersionMetadata.Xml.ContainingProject);

				// Take care how we remove the Version metadata, because it may be in an imported file or as part of an Update item.
				ProjectElementContainer parent = packageReferenceVersionMetadata.Xml.Parent;
				parent.RemoveChild(packageReferenceVersionMetadata.Xml);
				if (parent is ProjectItemElement { Update.Length: > 0, HasMetadata: false })
				{
					// The Update item has nothing left to offer. Remove it.
					parent.Parent.RemoveChild(parent);
				}
			}
		}

		if (project.IsDirty || changedFiles.Count > 0)
		{
			string versionOverrideWarning = versionOverrides > 0 ? $" (with {versionOverrides} version overrides)" : string.Empty;
			this.Out.WriteLine($"Migrated \"{relativeProjectPath}\"{versionOverrideWarning}");

			foreach (ProjectRootElement changedFile in changedFiles)
			{
				if (changedFile != project.Xml)
				{
					this.Out.WriteLine($"  {System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(this.DirectoryPackagesPropsPath)!, changedFile.FullPath)}");
					changedFile.Save();
				}
			}

			project.Save();
			directoryPackagesProps.Save();
		}
	}
}
