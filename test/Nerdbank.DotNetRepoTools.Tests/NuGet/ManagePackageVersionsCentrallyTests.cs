// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Evaluation;
using Nerdbank.DotNetRepoTools.NuGet;

namespace NuGet;

public class ManagePackageVersionsCentrallyTests : CommandTestBase<ManagePackageVersionsCentrally>
{
	private const ProjectLoadSettings DefaultProjectLoadSettings = ProjectLoadSettings.IgnoreMissingImports | ProjectLoadSettings.IgnoreInvalidImports;

	public ManagePackageVersionsCentrallyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	public override async Task InitializeAsync()
	{
		await base.InitializeAsync();
		await this.PlaceAssetsAsync("NonCPVM");
	}

	[Fact]
	public async Task PathDoesNotExist()
	{
		this.Command = new()
		{
			DirectoryPackagesPropsPath = Path.Combine(this.StagingDirectory, DirectoryPackagesPropsFileName),
			Path = Path.Combine(this.StagingDirectory, "DoesNotExist"),
		};

		await this.ExecuteCommandAsync();
		Assert.NotEqual(0, this.Command.ExitCode);
	}

	[Fact]
	public async Task MigrateOneProject()
	{
		this.Command = new()
		{
			DirectoryPackagesPropsPath = Path.Combine(this.StagingDirectory, DirectoryPackagesPropsFileName),
			Path = Path.Combine(this.StagingDirectory, @"NonCPVM", "ProjectWithVersionNumbers", "ProjectWithVersionNumbers.csproj"),
		};

		await this.ExecuteCommandAsync();
		Assert.Equal(0, this.Command.ExitCode);

		Assert.True(File.Exists(this.Command.DirectoryPackagesPropsPath));

		this.LogFileContent(this.Command.DirectoryPackagesPropsPath);

		Project projectWithVersionNumbers = this.MSBuild.GetProject(this.Command.Path, DefaultProjectLoadSettings);
		this.AssertPackageVersionItemsAreUsed(projectWithVersionNumbers);
	}

	[Fact]
	public async Task MigrateWholeRepo()
	{
		this.Command = new()
		{
			DirectoryPackagesPropsPath = Path.Combine(this.StagingDirectory, DirectoryPackagesPropsFileName),
			Path = this.StagingDirectory,
		};

		await this.ExecuteCommandAsync();
		Assert.Equal(0, this.Command.ExitCode);

		Assert.True(File.Exists(this.Command.DirectoryPackagesPropsPath));

		this.LogFileContent(this.Command.DirectoryPackagesPropsPath);

		string[] extensions = new[] { "*.csproj", "*.props", "*.targets" };
		foreach (string extension in extensions)
		{
			foreach (string projectFile in Directory.EnumerateFiles(Path.Combine(this.StagingDirectory, @"NonCPVM"), extension, SearchOption.AllDirectories))
			{
				Project project = this.MSBuild.GetProject(projectFile, DefaultProjectLoadSettings);
				this.AssertPackageVersionItemsAreUsed(project);
			}
		}
	}

	private void AssertPackageVersionItemsAreUsed(Project project)
	{
		this.LogFileContent(project.FullPath);

		bool isRealProject = project.FullPath.EndsWith("proj", StringComparison.OrdinalIgnoreCase);
		if (isRealProject)
		{
			Assert.Equal("true", project.GetPropertyValue("ManagePackageVersionsCentrally"), ignoreCase: true);
		}

		try
		{
			foreach (ProjectItem item in project.GetItemsIgnoringCondition("PackageReference"))
			{
				if (item.IsImported)
				{
					continue;
				}

				try
				{
					Assert.Null(item.GetMetadata("Version"));
					if (isRealProject)
					{
						Assert.NotEqual(string.Empty, MSBuild.FindItem(project, "PackageVersion", item.EvaluatedInclude)?.GetMetadata("Version")?.UnevaluatedValue ?? string.Empty);
					}
				}
				catch (Exception ex)
				{
					throw new Exception($"Failed while inspecting item \"{item.EvaluatedInclude}\".", ex);
				}
			}
		}
		catch (Exception ex)
		{
			throw new Exception($"Failed while inspecting project file \"{project.FullPath}\".", ex);
		}
	}
}
