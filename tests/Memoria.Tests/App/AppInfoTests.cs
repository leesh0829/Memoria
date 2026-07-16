using System;
using FluentAssertions;
using Memoria.App;
using Xunit;

namespace Memoria.Tests.App;

public class AppInfoTests
{
    [Fact]
    public void FormatVersion_formats_major_minor_patch_with_v_prefix()
        => AppInfo.FormatVersion(new Version(0, 8, 0, 0)).Should().Be("v0.8.0");

    [Fact]
    public void FormatVersion_ignores_revision_component()
        => AppInfo.FormatVersion(new Version(1, 2, 3, 4)).Should().Be("v1.2.3");

    [Fact]
    public void FormatVersion_null_returns_zero_version()
        => AppInfo.FormatVersion(null).Should().Be("v0.0.0");
}
