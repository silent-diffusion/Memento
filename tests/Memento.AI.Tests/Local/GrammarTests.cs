using System.Text.Json;
using Memento.AI.Local;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Local;

public sealed class GrammarTests
{
    private static readonly JsonElement ItemsSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "decisions": { "type": "array", "items": {
              "type": "object",
              "properties": { "text": { "type": "string" }, "segment": { "type": "integer" } },
              "required": ["text", "segment"], "additionalProperties": false } },
            "owner": { "type": ["string", "null"] },
            "status": { "enum": ["open", "done"] },
            "confidence": { "type": "number" },
            "supported": { "type": "boolean" }
          },
          "required": ["decisions", "owner", "status", "confidence", "supported"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    [Fact]
    public void ASchemaGrammarAcceptsCompactAndNaturallyIndentedJson()
    {
        var grammar = GbnfMatcher.Parse(JsonSchemaGrammar.FromSchema(ItemsSchema));

        Assert.True(grammar.Accepts("""{"decisions":[{"text":"Ship on Thursday","segment":12}],"owner":null,"status":"open","confidence":0.8,"supported":true}"""));
        Assert.True(grammar.Accepts("""
            {
              "decisions": [
                { "text": "Ship on \"Thursday\"", "segment": 12 },
                { "text": "Move templates", "segment": 40 }
              ],
              "owner": "Speaker B",
              "status": "done",
              "confidence": -1.5e3,
              "supported": false
            }
            """.ReplaceLineEndings("\n")));
        Assert.True(grammar.Accepts("""{"decisions":[],"owner":"x","status":"open","confidence":1,"supported":true}"""));
    }

    [Theory]
    [InlineData("""{"decisions":[],"owner":null,"status":"open","confidence":1}""")]
    [InlineData("""{"decisions":[],"owner":null,"status":"maybe","confidence":1,"supported":true}""")]
    [InlineData("""{"decisions":[{"text":"x","segment":1.5}],"owner":null,"status":"open","confidence":1,"supported":true}""")]
    [InlineData("""{"decisions":[],"owner":null,"status":"open","confidence":1,"supported":true,"extra":1}""")]
    [InlineData("""{"owner":null,"decisions":[],"status":"open","confidence":1,"supported":true}""")]
    [InlineData("""Here you go: {"decisions":[]}""")]
    [InlineData("""{"decisions":[],"owner":null,"status":"open","confidence":1,"supported":true}  trailing""")]
    public void ASchemaGrammarRejectsAnythingElse(string json)
    {
        var grammar = GbnfMatcher.Parse(JsonSchemaGrammar.FromSchema(ItemsSchema));

        Assert.False(grammar.Accepts(json));
    }

    [Fact]
    public void ArrayBoundsConstsAndAnyOfAreHonoured()
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "ids":{"type":"array","items":{"type":"integer"},"minItems":1,"maxItems":3},
              "kind":{"const":"verdict"},
              "value":{"anyOf":[{"type":"string","maxLength":4},{"type":"null"}]}
            },"required":["ids","kind","value"],"additionalProperties":false}
            """).RootElement;
        var grammar = GbnfMatcher.Parse(JsonSchemaGrammar.FromSchema(schema));

        Assert.True(grammar.Accepts("""{"ids":[1],"kind":"verdict","value":null}"""));
        Assert.True(grammar.Accepts("""{"ids":[1, 2, 3],"kind":"verdict","value":"abcd"}"""));
        Assert.False(grammar.Accepts("""{"ids":[],"kind":"verdict","value":null}"""));
        Assert.False(grammar.Accepts("""{"ids":[1,2,3,4],"kind":"verdict","value":null}"""));
        Assert.False(grammar.Accepts("""{"ids":[1],"kind":"other","value":null}"""));
        Assert.False(grammar.Accepts("""{"ids":[1],"kind":"verdict","value":"abcde"}"""));
    }

    [Fact]
    public void AnUntypedSchemaAcceptsAnyJsonValue()
    {
        var grammar = GbnfMatcher.Parse(JsonSchemaGrammar.FromSchema(JsonDocument.Parse("{}").RootElement));

        Assert.True(grammar.Accepts("""{"a":[1,true,null,{"b":"c"}]}"""));
        Assert.True(grammar.Accepts("\"text\""));
        Assert.False(grammar.Accepts("{a:1}"));
    }

    [Fact]
    public void ReferencesAreRefusedClearly() =>
        Assert.Throws<NotSupportedException>(() => JsonSchemaGrammar.FromSchema(JsonDocument.Parse("""{"$ref":"#/defs/x"}""").RootElement));

    [Fact]
    public void LiteralsEscapeQuotesBackslashesAndControlCharacters() =>
        Assert.Equal("\"\\\"a\\\\b\\n\\x01\"", JsonSchemaGrammar.Literal("\"a\\b\n\u0001"));

    [Fact]
    public void TheSpikeGrammarsParseAndAllowLineBreaks()
    {
        var items = GbnfMatcher.Parse(LocalGrammars.DecisionsAndActions);
        var verdict = GbnfMatcher.Parse(LocalGrammars.ClaimVerdict);
        var agenda = GbnfMatcher.Parse(LocalGrammars.AgendaCoverage);

        Assert.True(items.Accepts("""
            {"decisions": [
              {"decision": "Release 3.2 ships on Thursday, November 12.", "citation": {"start": 101.5, "end": 112.25, "quote": "we ship on the twelfth"}}
            ],
            "action_items": [
              {"task": "Cut the 3.3 branch", "owner": "Speaker B", "due": null, "citation": {"start": 300, "end": 304, "quote": "I'll cut it Friday"}}
            ]}
            """.ReplaceLineEndings("\n")));
        Assert.True(items.Accepts("""{"decisions":[],"action_items":[]}"""));
        Assert.True(verdict.Accepts("""{"reason": "The excerpt names the date.", "supported": true}"""));
        Assert.False(verdict.Accepts("""{"reason": "x", "supported": "yes"}"""));
        Assert.True(agenda.Accepts("""{"items":[{"id":1,"discussed":true,"quote":"pricing page"},{"id":2,"discussed":false,"quote":null}]}"""));
    }

    [Fact]
    public void GrammarsForbidingLineBreaksAreNotProduced()
    {
        var grammar = JsonSchemaGrammar.FromSchema(ItemsSchema);

        Assert.Contains("ws ::= | \" \" | \"\\n\" [ \\t]{0,24}", grammar, StringComparison.Ordinal);
        Assert.StartsWith("root ::= r\n", grammar, StringComparison.Ordinal);
    }
}
