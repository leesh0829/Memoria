using System;
using System.IO;
using FluentAssertions;
using Memoria.App;
using Xunit;

namespace Memoria.Tests.App;

public class AppPathsTests
{
    [Fact]
    public void DataDirectory_is_under_LocalApplicationData_Memoria()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppPaths.DataDirectory.Should().Be(Path.Combine(local, "Memoria"));
    }

    [Fact]
    public void DatabaseFile_and_RecoveryDirectory_are_under_DataDirectory()
    {
        AppPaths.DatabaseFile.Should().Be(Path.Combine(AppPaths.DataDirectory, "memoria.db"));
        AppPaths.RecoveryDirectory.Should().Be(Path.Combine(AppPaths.DataDirectory, "recovery"));
    }
}
