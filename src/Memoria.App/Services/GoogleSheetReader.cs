using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Memoria.Core;
using Memoria.Core.Data;
using Memoria.Core.Sheets;

namespace Memoria.App.Services;

/// <summary>서비스 계정 JSON으로 인증 후 Sheets API(A:C, 읽기 전용)로 셀 격자를 읽는다.</summary>
public sealed class GoogleSheetReader : ISpreadsheetReader
{
    private readonly ISettingsRepository _settings;
    public GoogleSheetReader(ISettingsRepository settings) => _settings = settings;

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ReadRowsAsync(
        string sheetId, string tabName, CancellationToken ct = default)
    {
        var jsonPath = _settings.GetOrDefault(SettingsKeys.GoogleServiceAccountJsonPath, "");
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            throw new InvalidOperationException("서비스 계정 JSON 키 경로가 설정되지 않았거나 파일이 없습니다.");

        var credential = GoogleCredential.FromFile(jsonPath)
            .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);

        using var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Memoria",
        });

        var request = service.Spreadsheets.Values.Get(sheetId, $"{tabName}!A:C");
        var response = await request.ExecuteAsync(ct).ConfigureAwait(false);

        var grid = new List<IReadOnlyList<string>>();
        if (response.Values is null) return grid;
        foreach (var row in response.Values)
        {
            var cells = new List<string>(row.Count);
            foreach (var cell in row) cells.Add(cell?.ToString() ?? "");
            grid.Add(cells);
        }
        return grid;
    }
}
