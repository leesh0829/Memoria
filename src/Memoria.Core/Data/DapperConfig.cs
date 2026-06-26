using System.Data;
using System.Globalization;
using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

internal static class DapperConfig
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new NoteTypeHandler());
        SqlMapper.AddTypeHandler(new ItemKindHandler());
        SqlMapper.AddTypeHandler(new ReportFormatKindHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) =>
            DateOnly.ParseExact((string)value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) =>
            DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private sealed class NoteTypeHandler : SqlMapper.TypeHandler<NoteType>
    {
        public override NoteType Parse(object value) => (string)value switch
        {
            "plain" => NoteType.Plain,
            "checklist" => NoteType.Checklist,
            "weekly_report" => NoteType.WeeklyReport,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown note type"),
        };

        public override void SetValue(IDbDataParameter parameter, NoteType value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value switch
            {
                NoteType.Plain => "plain",
                NoteType.Checklist => "checklist",
                NoteType.WeeklyReport => "weekly_report",
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
        }
    }

    private sealed class ItemKindHandler : SqlMapper.TypeHandler<ItemKind>
    {
        public override ItemKind Parse(object value) => (string)value switch
        {
            "task" => ItemKind.Task,
            "issue" => ItemKind.Issue,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown item kind"),
        };

        public override void SetValue(IDbDataParameter parameter, ItemKind value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value == ItemKind.Task ? "task" : "issue";
        }
    }

    private sealed class ReportFormatKindHandler : SqlMapper.TypeHandler<ReportFormatKind>
    {
        public override ReportFormatKind Parse(object value) => (string)value switch
        {
            "A" => ReportFormatKind.A,
            "B" => ReportFormatKind.B,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown report format"),
        };

        public override void SetValue(IDbDataParameter parameter, ReportFormatKind value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value == ReportFormatKind.A ? "A" : "B";
        }
    }
}
