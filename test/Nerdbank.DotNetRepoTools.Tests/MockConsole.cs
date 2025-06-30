// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

public class MockConsole
{
	public StringWriter Out { get; } = new StringWriter();

	public StringWriter Error { get; } = new StringWriter();
}
