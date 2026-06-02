// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.NuGet;

/// <summary>
/// The supported output formats for packing project results.
/// </summary>
public enum PackingProjectsOutputFormat
{
	/// <summary>
	/// Human-readable text output.
	/// </summary>
	Text,

	/// <summary>
	/// JSON output.
	/// </summary>
	Json,
}
