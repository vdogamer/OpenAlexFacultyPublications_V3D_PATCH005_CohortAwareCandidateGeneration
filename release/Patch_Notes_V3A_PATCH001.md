# Patch Notes — Version 3A PATCH001

## Fix

Added:

```csharp
global using Xunit;
```

to:

```text
tests/OpenAlex.Tests/GlobalUsings.cs
```

## Problem

The V3A test files used xUnit attributes:

- `Fact`
- `Theory`
- `InlineData`

but did not import the xUnit namespace.

## Result

The test project can now resolve xUnit attributes and assertions.
