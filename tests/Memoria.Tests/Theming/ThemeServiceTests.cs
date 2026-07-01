// tests/Memoria.Tests/Theming/ThemeServiceTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Memoria.Core;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Xunit;

namespace Memoria.Tests.Theming;

public class ThemeServiceTests
{
    private static (ThemeService svc, FakeThemeApplier applier, FakeSystemThemeSource sys, InMemorySettingsRepository settings)
        Create(bool systemLight = true)
    {
        var settings = new InMemorySettingsRepository();
        var applier = new FakeThemeApplier();
        var sys = new FakeSystemThemeSource { Light = systemLight };
        var svc = new ThemeService(settings, applier, sys);
        return (svc, applier, sys, settings);
    }

    [Fact]
    public void Initialize_uses_defaults_and_applies_palette()
    {
        var (svc, applier, _, _) = Create(systemLight: true);

        svc.Initialize();

        svc.Mode.Should().Be(ThemeMode.System);
        svc.Preset.Should().Be("default");
        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Light.xaml");
    }

    [Fact]
    public void Initialize_reads_persisted_values()
    {
        var (svc, applier, _, settings) = Create(systemLight: true);
        settings.Set(SettingsKeys.ThemeMode, "dark");
        settings.Set(SettingsKeys.ThemePreset, "blue");

        svc.Initialize();

        svc.Mode.Should().Be(ThemeMode.Dark);
        svc.Preset.Should().Be("blue");
        applier.LastPalette!.OriginalString.Should().Be("Themes/Blue.Dark.xaml");
    }

    [Fact]
    public void Apply_persists_normalized_settings()
    {
        var (svc, _, _, settings) = Create();

        svc.Apply(ThemeMode.Light, "Green");

        settings.Get(SettingsKeys.ThemeMode).Should().Be("light");
        settings.Get(SettingsKeys.ThemePreset).Should().Be("green");
    }

    [Fact]
    public void Apply_fixed_mode_ignores_system_value()
    {
        var (svc, applier, _, _) = Create(systemLight: true);

        svc.Apply(ThemeMode.Dark, "default");

        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Dark.xaml");
    }

    [Fact]
    public void System_change_reapplies_only_when_mode_is_system()
    {
        var (svc, applier, sys, _) = Create(systemLight: true);
        svc.Apply(ThemeMode.System, "default");
        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Light.xaml");

        sys.Light = false;
        sys.RaiseChanged();

        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Dark.xaml");
    }

    [Fact]
    public void System_change_is_ignored_for_fixed_mode()
    {
        var (svc, applier, sys, _) = Create(systemLight: true);
        svc.Apply(ThemeMode.Light, "default");
        var countAfterApply = applier.PaletteApplyCount;

        sys.Light = false;
        sys.RaiseChanged();

        applier.PaletteApplyCount.Should().Be(countAfterApply); // 재적용 없음
        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Light.xaml");
    }

    [Fact]
    public void Apply_raises_ThemeChanged()
    {
        var (svc, _, _, _) = Create();
        var raised = false;
        svc.ThemeChanged += (_, _) => raised = true;

        svc.Apply(ThemeMode.Dark, "default");

        raised.Should().BeTrue();
    }

    [Theory]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("DARK", ThemeMode.Dark)]
    [InlineData("system", ThemeMode.System)]
    [InlineData("garbage", ThemeMode.System)]
    [InlineData(null, ThemeMode.System)]
    public void ParseMode_maps_strings(string? input, ThemeMode expected)
    {
        ThemeService.ParseMode(input).Should().Be(expected);
    }
}
