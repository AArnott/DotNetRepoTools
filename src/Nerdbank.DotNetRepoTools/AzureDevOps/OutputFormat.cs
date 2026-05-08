// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// The output format for Azure DevOps commands that support text and JSON output.
/// </summary>
internal enum OutputFormat
{
	/// <summary>
	/// Human-readable text output (default).
	/// </summary>
	Text,

	/// <summary>
	/// JSON output as returned by the Azure DevOps REST API.
	/// </summary>
	Json,
}
