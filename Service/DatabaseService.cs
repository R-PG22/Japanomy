using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JapanomyApp.Models;
using SQLite;

namespace JapanomyApp.Services;

public enum MatchMode
{
    Exact = 0,
    StartsWith = 1,
    EndsWith = 2,
    Contains = 3
}

public class SearchOptions
{
    public MatchMode Mode { get; set; } = MatchMode.Exact;
    public bool IgnoreN { get; set; } = false;
    public bool IgnoreChoon { get; set; } = false;
}

public class KanjiSearchOptions
{
    public MatchMode Mode { get; set; } = MatchMode.Exact;
    public bool ExactKanjiOnly { get; set; } = false; // true: 漢字のみ / false: ひらがな等も含めてOK
}

public class SenseResult
{
    [Column("gloss")]
    public string? Gloss { get; set; }
}

public class DatabaseService
{
    private SQLiteAsyncConnection? _database;
    private static readonly HttpClient _httpClient = new();

    // アプリ実行中のみメモリ上に保持される翻訳キャッシュ（ent_seq -> 翻訳結果）
    private static readonly Dictionary<int, string> _senseCache = new();

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_database != null)
            return _database;

        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "jmdict.db");
        _database = new SQLiteAsyncConnection(dbPath);
        return _database;
    }

    /// <summary>
    /// 読み検索（母音・ローマ字パターン検索）
    /// </summary>
    public async Task<List<ReadingItem>> SearchAdvancedAsync(string pattern, SearchOptions options)
    {
        var db = await GetConnectionAsync();

        if (string.IsNullOrWhiteSpace(pattern))
            return new List<ReadingItem>();

        string processed = pattern.Replace("nn", "n");
        processed = processed.Replace("・", "_").Replace("*", "%");

        string cleanPattern = processed.Replace("_", "").Replace("%", "");
        bool isVowelOnly = !string.IsNullOrEmpty(cleanPattern) && Regex.IsMatch(cleanPattern, @"^[aiueo]+$");

        string targetColumn = isVowelOnly ? "r.vowels" : "r.romaji";

        string sqlPattern = options.Mode switch
        {
            MatchMode.StartsWith => $"{processed}%",
            MatchMode.EndsWith   => $"%{processed}",
            MatchMode.Contains   => $"%{processed}%",
            MatchMode.Exact      => processed,
            _ => processed
        };

        var whereClauses = new List<string> { $"{targetColumn} LIKE ?" };

        if (options.IgnoreN)
        {
            whereClauses.Add("r.reb NOT LIKE '%ん%'");
            whereClauses.Add("r.romaji NOT LIKE '%n%'");
        }

        if (options.IgnoreChoon)
        {
            whereClauses.Add("r.reb NOT LIKE '%ー%'");
            whereClauses.Add("r.romaji NOT LIKE '%-%'");
        }

        string whereSql = string.Join(" AND ", whereClauses);

        string query = $@"
            SELECT 
                r.rowid, 
                r.ent_seq, 
                r.reb, 
                r.vowels, 
                r.romaji, 
                COALESCE(k.keb, '（表記なし）') AS Kanji 
            FROM readings r
            LEFT JOIN kanji k ON r.ent_seq = k.ent_seq
            WHERE {whereSql}
            GROUP BY r.rowid";

        return await db.QueryAsync<ReadingItem>(query, sqlPattern);
    }

    /// <summary>
    /// 漢字検索
    /// </summary>
    public async Task<List<ReadingItem>> SearchKanjiAsync(string pattern, KanjiSearchOptions options)
    {
        var db = await GetConnectionAsync();

        if (string.IsNullOrWhiteSpace(pattern))
            return new List<ReadingItem>();

        // ・ を _ (任意の1文字)、* を % (任意の0文字以上) に置換
        string processed = pattern.Replace("・", "_").Replace("*", "%");

        string sqlPattern = options.Mode switch
        {
            MatchMode.StartsWith => $"{processed}%",
            MatchMode.EndsWith   => $"%{processed}",
            MatchMode.Contains   => $"%{processed}%",
            MatchMode.Exact      => processed,
            _ => processed
        };

        var whereClauses = new List<string> { "k.keb LIKE ?" };

        // 「漢字のみ」の条件指定（ひらがな・カタカナを含まない表記に絞り込む）
        if (options.ExactKanjiOnly)
        {
            whereClauses.Add("k.keb NOT GLOB '*[ぁ-んァ-ヶ]*'");
        }

        string whereSql = string.Join(" AND ", whereClauses);

        string query = $@"
            SELECT 
                r.rowid, 
                r.ent_seq, 
                r.reb, 
                r.vowels, 
                r.romaji, 
                k.keb AS Kanji 
            FROM kanji k
            INNER JOIN readings r ON k.ent_seq = r.ent_seq
            WHERE {whereSql}
            GROUP BY k.keb, r.reb";

        return await db.QueryAsync<ReadingItem>(query, sqlPattern);
    }

    /// <summary>
    /// 選択時に意味を取得（キャッシュがあれば即返却、なければ翻訳してキャッシュ保存）
    /// </summary>
    public async Task<string> GetSenseAsync(int entSeq)
    {
        // 1. キャッシュが存在する場合は即座に返却（0秒）
        if (_senseCache.TryGetValue(entSeq, out var cachedSense))
        {
            return cachedSense;
        }

        var db = await GetConnectionAsync();

        try
        {
            string senseQuery = "SELECT gloss FROM senses WHERE ent_seq = ?";
            var senseList = await db.QueryAsync<SenseResult>(senseQuery, entSeq);
            if (senseList.Count > 0)
            {
                var glosses = new List<string>();
                foreach (var item in senseList)
                {
                    if (!string.IsNullOrEmpty(item.Gloss))
                    {
                        glosses.Add(item.Gloss!);
                    }
                }

                if (glosses.Count > 0)
                {
                    string rawEnglish = string.Join(" / ", glosses);
                    string translated = await TranslateToJapaneseAsync(rawEnglish);
                    string resultText = $"{translated} ({rawEnglish})";

                    // 2. 翻訳結果をメモリキャッシュに保存
                    _senseCache[entSeq] = resultText;

                    return resultText;
                }
            }
        }
        catch { /* 無視 */ }

        return "（意味データなし）";
    }

    private async Task<string> TranslateToJapaneseAsync(string text)
    {
        try
        {
            string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=en|ja";
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            
            var matches = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
            if (!string.IsNullOrWhiteSpace(matches))
            {
                return matches;
            }
        }
        catch { }

        return text;
    }
}