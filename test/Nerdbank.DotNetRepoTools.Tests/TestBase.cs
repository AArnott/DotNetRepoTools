// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Reflection;

public abstract class TestBase : IAsyncLifetime
{
	private readonly List<string> tempFiles = new();
	private readonly List<string> tempDirectories = new();

	public TestBase(ITestOutputHelper logger)
	{
		this.Logger = logger;
		this.StagingDirectory = Path.Combine(Path.GetTempPath(), "test_" + Path.GetRandomFileName());
		this.RegisterTemporaryDirectory(this.StagingDirectory);
	}

	public string StagingDirectory { get; }

	public ITestOutputHelper Logger { get; }

	internal MSBuild MSBuild { get; } = new();

	public virtual Task InitializeAsync()
	{
		return Task.CompletedTask;
	}

	public virtual Task DisposeAsync()
	{
		foreach (string file in this.tempFiles)
		{
			File.Delete(file);
		}

		foreach (string dir in this.tempDirectories)
		{
			Directory.Delete(dir, recursive: true);
		}

		return Task.CompletedTask;
	}

	internal static Stream GetAsset(string assetName)
	{
		return Assembly.GetExecutingAssembly().GetManifestResourceStream($"Assets.{assetName.Replace('/', '.')}")
			?? throw new ArgumentException($"No resource named {assetName} found under the Assets directory of the test project.");
	}

	protected void DumpConsole(IConsole console)
	{
		if (console.Out.ToString() is { Length: > 0 } stdout)
		{
			this.Logger.WriteLine("Command STDOUT:");
			this.Logger.WriteLine(stdout);
		}

		if (console.Error.ToString() is { Length: > 0 } stderr)
		{
			this.Logger.WriteLine("Command STDERR:");
			this.Logger.WriteLine(stderr);
		}
	}

	protected async Task<string> PlaceAssetAsync(string assetName, string? baseDirectory = null, CancellationToken cancellationToken = default)
	{
		baseDirectory ??= this.StagingDirectory;
		string targetFilePath = Path.Combine(baseDirectory, assetName);
		string? targetDirectory = Path.GetDirectoryName(targetFilePath);
		if (targetDirectory is not null)
		{
			Directory.CreateDirectory(targetDirectory);
		}

		using Stream assetStream = GetAsset(assetName);
		using FileStream targetFile = File.Create(targetFilePath);
		this.RegisterTemporaryFile(targetFilePath);
		await assetStream.CopyToAsync(targetFile, cancellationToken);

		return targetFilePath;
	}

	protected void RegisterTemporaryFile(string path) => this.tempFiles.Add(path);

	protected void RegisterTemporaryDirectory(string path) => this.tempDirectories.Add(path);
}
