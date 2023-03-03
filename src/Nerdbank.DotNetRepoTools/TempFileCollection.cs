// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// A collection of file system paths to delete when disposed.
/// </summary>
public class TempFileCollection : IDisposable
{
	private readonly List<string> tempPaths = new();

	/// <summary>
	/// Tracks a file or directory for deletion when this collection is disposed.
	/// </summary>
	/// <param name="path">The path to the file or directory.</param>
	public void Add(string path) => this.tempPaths.Add(path);

	/// <inheritdoc/>
	public void Dispose()
	{
		foreach (string path in this.tempPaths)
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
			else if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
	}
}
