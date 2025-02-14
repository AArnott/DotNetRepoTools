// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable SA1602 // Enumeration items should be documented

using System.Text.Json.Serialization;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum JsonPatchOperation
{
#pragma warning disable SA1300 // Element should begin with upper-case letter
	add,
	copy,
	move,
	remove,
	replace,
	test,
#pragma warning restore SA1300 // Element should begin with upper-case letter
}
