using System;
using System.Collections.Generic;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.App;

public class NoteTitleResolverTests
{
    [Fact]
    public void Uses_title_when_present()
    {
        var note = new Note { Title = "  명시 제목 ", Body = "첫 줄" };
        NoteTitleResolver.Resolve(note).Should().Be("명시 제목");
    }

    [Fact]
    public void Falls_back_to_first_nonempty_body_line_when_title_blank()
    {
        var note = new Note { Title = "   ", Body = "\n\n  본문 첫 줄\n둘째 줄" };
        NoteTitleResolver.Resolve(note).Should().Be("본문 첫 줄");
    }

    [Fact]
    public void Returns_placeholder_when_title_and_body_empty()
    {
        var note = new Note { Title = null, Body = "" };
        NoteTitleResolver.Resolve(note).Should().Be("(제목 없음)");
    }

    [Fact]
    public void Markdown_note_strips_leading_markers_from_body_title()
    {
        var note = new Note { Title = null, Body = "# 제목\n본문", BodyFormat = "markdown" };
        NoteTitleResolver.Resolve(note).Should().Be("제목");
    }

    [Fact]
    public void Plain_note_keeps_markdown_like_body_line_verbatim()
    {
        var note = new Note { Title = null, Body = "# 제목", BodyFormat = "plain" };
        NoteTitleResolver.Resolve(note).Should().Be("# 제목");
    }

    [Fact]
    public void Checklist_with_log_date_shows_date_title()
    {
        var note = new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) };
        NoteTitleResolver.Resolve(note).Should().Be("2026-07-06 (월)");
    }

    [Theory]
    [InlineData(2026, 7, 5, "일")]   // Sunday
    [InlineData(2026, 7, 6, "월")]
    [InlineData(2026, 7, 11, "토")]  // Saturday
    public void Checklist_weekday_uses_fixed_korean_array(int y, int m, int d, string wd)
    {
        var note = new Note { Type = NoteType.Checklist, LogDate = new DateOnly(y, m, d) };
        NoteTitleResolver.Resolve(note).Should().Be($"{y:0000}-{m:00}-{d:00} ({wd})");
    }

    [Fact]
    public void Checklist_date_title_takes_precedence_over_body()
    {
        var note = new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6), Body = "무시" };
        NoteTitleResolver.Resolve(note).Should().Be("2026-07-06 (월)");
    }

    [Fact]
    public void Plain_note_with_log_date_is_not_date_titled()
    {
        var note = new Note { Type = NoteType.Plain, LogDate = new DateOnly(2026, 7, 6), Body = "본문" };
        NoteTitleResolver.Resolve(note).Should().Be("본문");
    }

    [Fact]
    public void Checklist_without_log_date_falls_back_to_placeholder()
    {
        var note = new Note { Type = NoteType.Checklist, LogDate = null };
        NoteTitleResolver.Resolve(note).Should().Be("(제목 없음)");
    }

    [Fact]
    public void ResolveList_suffixes_duplicate_dates_by_id_order()
    {
        var notes = new List<Note>
        {
            new() { Id = 5, Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) },
            new() { Id = 2, Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) },
            new() { Id = 9, Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 6) },
            new() { Id = 3, Type = NoteType.Checklist, LogDate = new DateOnly(2026, 7, 7) },
        };
        var titles = NoteTitleResolver.ResolveList(notes);
        // 입력 순서 보존, id 오름차순으로 접미사(2:정본, 5:(2), 9:(3))
        titles[0].Should().Be("2026-07-06 (월) (2)");   // id 5
        titles[1].Should().Be("2026-07-06 (월)");       // id 2 (MIN → 정본)
        titles[2].Should().Be("2026-07-06 (월) (3)");   // id 9
        titles[3].Should().Be("2026-07-07 (화)");       // id 3 유일
    }
}
