using SQLite;

namespace JapanomyApp.Models;

public class ReadingItem
{
    [Column("rowid")]
    public int RowId { get; set; }

    [Column("ent_seq")]
    public int EntSeq { get; set; }

    [Column("reb")]
    public string Reading { get; set; } = "";

    [Column("romaji")]
    public string Romaji { get; set; } = "";

    [Column("vowels")]
    public string Vowels { get; set; } = "";

    // 検索結果に紐付ける漢字表記
    public string Kanji { get; set; } = "";
}