// tests/Memoria.Tests/ViewModels/SettingsViewModelTests.cs
using FluentAssertions;
using Memoria.App.Theming;
using Memoria.App.ViewModels;
using Memoria.App.Windows;
using Memoria.Core;
using Memoria.Core.Models;
using Memoria.Tests.Fakes;
using Memoria.Tests.Theming;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class SettingsViewModelTests
{
    private sealed class FakeAutostartService : IAutostartService
    {
        public bool Enabled { get; private set; }
        public bool IsEnabled() => Enabled;
        public void Enable() => Enabled = true;
        public void Disable() => Enabled = false;
    }

    private static (SettingsViewModel vm, InMemorySettingsRepository settings, FakeAutostartService autostart, FakeThemeApplier applier)
        Create()
    {
        var settings = new InMemorySettingsRepository();
        var applier = new FakeThemeApplier();
        var theme = new ThemeService(settings, applier, new FakeSystemThemeSource { Light = true });
        theme.Initialize();
        var autostart = new FakeAutostartService();
        var vm = new SettingsViewModel(settings, theme, autostart);
        return (vm, settings, autostart, applier);
    }

    [Fact]
    public void Loads_defaults_when_settings_empty()
    {
        var (vm, _, _, _) = Create();

        vm.Mode.Should().Be(ThemeMode.System);
        vm.Preset.Should().Be("default");
        vm.Accent.Should().Be("#0078D4");
        vm.ReporterName.Should().Be("이승현");
        vm.TaskHeaderA.Should().Be("[업무 내용]");
        vm.IssueHeaderA.Should().Be("[이슈]");
        vm.TitleWordB.Should().Be("주간 보고");
        vm.IssueHeaderB.Should().Be("* 이슈사항:");
        vm.ReportIndent.Should().Be("\t");
        vm.IncludeDoneOnly.Should().BeFalse();
        vm.HotkeyNewNote.Should().Be("Ctrl+Alt+N");
        vm.Autostart.Should().BeTrue();
        vm.CloseToTray.Should().BeTrue();
        vm.BackupRetentionCount.Should().Be(7);
        vm.TrashRetentionDays.Should().Be(30);
    }

    [Fact]
    public void Loads_persisted_values()
    {
        var (vm, settings, _, _) = Create();
        settings.Set(SettingsKeys.ReporterName, "홍길동");
        settings.Set(SettingsKeys.IncludeDoneOnly, "true");
        settings.Set(SettingsKeys.TrashRetentionDays, "14");

        var reloaded = new SettingsViewModel(settings,
            new ThemeService(settings, new FakeThemeApplier(), new FakeSystemThemeSource()),
            new FakeAutostartService());

        reloaded.ReporterName.Should().Be("홍길동");
        reloaded.IncludeDoneOnly.Should().BeTrue();
        reloaded.TrashRetentionDays.Should().Be(14);
    }

    [Fact]
    public void Changing_mode_applies_theme_immediately()
    {
        var (vm, settings, _, applier) = Create();

        vm.Mode = ThemeMode.Dark;

        applier.LastPalette!.OriginalString.Should().Be("Themes/Default.Dark.xaml");
        settings.Get(SettingsKeys.ThemeMode).Should().Be("dark");
    }

    [Fact]
    public void Changing_valid_accent_applies_immediately()
    {
        var (vm, settings, _, applier) = Create();

        vm.Accent = "#FF0000";

        applier.LastAccent.Should().Be("#FF0000");
        settings.Get(SettingsKeys.ThemeAccent).Should().Be("#FF0000");
    }

    [Fact]
    public void Invalid_accent_is_not_applied_and_blocks_save()
    {
        var (vm, settings, _, applier) = Create();
        applier.LastAccent = null;

        vm.Accent = "nope";

        vm.IsAccentValid.Should().BeFalse();
        vm.CanSave.Should().BeFalse();
        applier.LastAccent.Should().BeNull(); // 무효색 미적용
    }

    [Fact]
    public void Invalid_hotkey_blocks_save()
    {
        var (vm, _, _, _) = Create();

        vm.HotkeyNewNote = "Ctrl+";

        vm.IsHotkeyValid.Should().BeFalse();
        vm.CanSave.Should().BeFalse();
    }

    [Fact]
    public void Save_persists_report_app_and_retention_keys()
    {
        var (vm, settings, autostart, _) = Create();
        vm.ReporterName = "김철수";
        vm.TaskHeaderA = "[할 일]";
        vm.IncludeDoneOnly = true;
        vm.HotkeyNewNote = "Ctrl+Shift+M";
        vm.Autostart = false;
        vm.CloseToTray = false;
        vm.BackupRetentionCount = 10;
        vm.TrashRetentionDays = 60;

        vm.SaveCommand.Execute(null);

        settings.Get(SettingsKeys.ReporterName).Should().Be("김철수");
        settings.Get(SettingsKeys.FormatATaskHeader).Should().Be("[할 일]");
        settings.Get(SettingsKeys.IncludeDoneOnly).Should().Be("true");
        settings.Get(SettingsKeys.HotkeyNewNote).Should().Be("Ctrl+Shift+M");
        settings.Get(SettingsKeys.Autostart).Should().Be("false");
        settings.Get(SettingsKeys.CloseToTray).Should().Be("false");
        settings.Get(SettingsKeys.BackupRetentionCount).Should().Be("10");
        settings.Get(SettingsKeys.TrashRetentionDays).Should().Be("60");
    }

    [Fact]
    public void Save_toggles_autostart_service()
    {
        var (vm, _, autostart, _) = Create();

        vm.Autostart = false;
        vm.SaveCommand.Execute(null);
        autostart.IsEnabled().Should().BeFalse();

        vm.Autostart = true;
        vm.SaveCommand.Execute(null);
        autostart.IsEnabled().Should().BeTrue();
    }

    [Fact]
    public void AvailablePresets_matches_resolver()
    {
        var (vm, _, _, _) = Create();
        vm.AvailablePresets.Should().BeEquivalentTo(new[] { "default", "dark", "sepia", "solarized" });
    }
}
