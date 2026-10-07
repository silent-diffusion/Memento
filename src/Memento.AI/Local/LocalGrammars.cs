namespace Memento.AI.Local;

/// <summary>
/// The GBNF grammars verified with Qwen3.5-4B and Ministral-3-3B in the October 2026 spike (map, verify, agenda),
/// kept verbatim for the generation pipeline (M4d). Whitespace is natural (space, or a line break and indentation).
/// Timestamps in <see cref="DecisionsAndActions"/> are seconds; the pipeline may switch them to short segment ids.
/// </summary>
public static class LocalGrammars
{
    public const string Common = """
        str ::= "\"" chr* "\""
        chr ::= [^"\\\x7F\x00-\x1F] | "\\" ["\\/bfnrt]
        nstr ::= str | "null"
        num ::= [0-9] [0-9]? [0-9]? [0-9]? [0-9]? ("." [0-9] [0-9]?)?
        ws ::= | " " | "\n" [ \t]{0,24}
        """;

    /// <summary><c>{"decisions":[{"decision","citation"}],"action_items":[{"task","owner","due","citation"}]}</c>.</summary>
    public const string DecisionsAndActions = """
        root ::= "{" ws "\"decisions\"" ws ":" ws "[" ws ( decision ( ws "," ws decision )* )? ws "]" ws "," ws "\"action_items\"" ws ":" ws "[" ws ( action ( ws "," ws action )* )? ws "]" ws "}"
        decision ::= "{" ws "\"decision\"" ws ":" ws str ws "," ws "\"citation\"" ws ":" ws citation ws "}"
        action ::= "{" ws "\"task\"" ws ":" ws str ws "," ws "\"owner\"" ws ":" ws nstr ws "," ws "\"due\"" ws ":" ws nstr ws "," ws "\"citation\"" ws ":" ws citation ws "}"
        citation ::= "{" ws "\"start\"" ws ":" ws num ws "," ws "\"end\"" ws ":" ws num ws "," ws "\"quote\"" ws ":" ws str ws "}"

        """ + Common;

    /// <summary><c>{"reason":"…","supported":true|false}</c>.</summary>
    public const string ClaimVerdict = """
        root ::= "{" ws "\"reason\"" ws ":" ws str ws "," ws "\"supported\"" ws ":" ws ( "true" | "false" ) ws "}"

        """ + Common;

    /// <summary><c>{"items":[{"id":1-9,"discussed":true|false,"quote":"…"|null}]}</c>.</summary>
    public const string AgendaCoverage = """
        root ::= "{" ws "\"items\"" ws ":" ws "[" ws item ( ws "," ws item )* ws "]" ws "}"
        item ::= "{" ws "\"id\"" ws ":" ws [1-9] ws "," ws "\"discussed\"" ws ":" ws ( "true" | "false" ) ws "," ws "\"quote\"" ws ":" ws nstr ws "}"

        """ + Common;
}
