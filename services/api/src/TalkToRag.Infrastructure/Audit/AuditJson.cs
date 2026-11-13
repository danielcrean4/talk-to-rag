using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TalkToRag.Infrastructure.Audit;

/// <summary>Shapes AI service traffic into JSON worth keeping: small enough to store, and with a copy safe to show an auditor.</summary>
public static class AuditJson
{
    /// <summary>Bodies larger than this (a big ingestion response is tens of megabytes) are recorded by size only.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    /// <summary>Parses a JSON or server-sent-events body, dropping embedding vectors and ingested chunk lists.</summary>
    public static JsonNode ToNode(string? body, bool isEventStream = false)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new JsonObject();
        }

        if (Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
        {
            return new JsonObject { ["omitted"] = true, ["bytes"] = Encoding.UTF8.GetByteCount(body) };
        }

        var node = isEventStream ? ParseEvents(body) : TryParse(body) ?? new JsonObject { ["text"] = body };
        return Slim(node);
    }

    /// <summary>A run of this many letters and digits copied from withheld text counts as a quote of it.</summary>
    public const int QuoteCharacters = 40;

    /// <summary>
    /// Returns copies of the request and response with source text replaced by placeholders
    /// wherever it appears: the passages the request carried, earlier answers sent as context
    /// (they can quote passages), the prompts in the trace, and runs the model copied into its answer.
    /// </summary>
    public static (JsonNode Request, JsonNode Response) Redact(JsonNode request, JsonNode? response)
    {
        var withheld = new List<Withheld>();
        if (request is JsonObject body)
        {
            foreach (var item in (body["passages"] as JsonArray ?? []).OfType<JsonObject>())
            {
                Add(withheld, (item["content"] ?? item["text"])?.GetValue<string>(), $"passage {item["n"]}");
            }

            // Text sent to be read aloud is a summary, which can quote passages.
            if (body["text"] is JsonValue spoken && spoken.GetValueKind() == JsonValueKind.String)
            {
                Add(withheld, spoken.GetValue<string>(), "text to speak");
            }

            foreach (var item in (body["recent_messages"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (item["role"]?.GetValue<string>() == "assistant")
                {
                    Add(withheld, item["content"]?.GetValue<string>(), "earlier answer");
                }
            }
        }

        // Longest first, so a text that contains another is replaced whole.
        withheld.Sort((a, b) => b.Text.Length.CompareTo(a.Text.Length));
        var quotes = new Dictionary<string, (Withheld Source, int Index)>(StringComparer.Ordinal);
        foreach (var source in withheld)
        {
            for (var i = 0; i + QuoteCharacters <= source.Letters.Length; i++)
            {
                quotes.TryAdd(source.Letters.Substring(i, QuoteCharacters), (source, i));
            }
        }

        string Clean(string text) => WithholdQuotes(
            withheld.Aggregate(text, (current, source) =>
                current.Replace(source.Text, $"[{source.Name} withheld, {source.Text.Length} characters]", StringComparison.Ordinal)),
            quotes);

        return (Replace(request, Clean)!, Replace(response, Clean) ?? new JsonObject());
    }

    private static void Add(List<Withheld> withheld, string? text, string name)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            withheld.Add(new Withheld(name, text.Trim()));
        }
    }

    public static string Serialize(JsonNode node) => node.ToJsonString(Compact);

    private static JsonNode? TryParse(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonObject ParseEvents(string body)
    {
        var events = new JsonArray();
        var answer = new StringBuilder();
        JsonNode trace = new JsonArray();
        foreach (var line in body.Split('\n'))
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal) || TryParse(line[6..]) is not JsonObject data)
            {
                continue;
            }

            // Tokens are joined into one answer; one event per token would bury everything else.
            if (data["token"] is JsonValue token)
            {
                answer.Append(token.GetValue<string>());
            }
            else if (data["trace"] is JsonArray calls)
            {
                // Same place as in a plain JSON response, so one query reads both.
                trace = calls.DeepClone();
            }
            else
            {
                events.Add(data);
            }
        }

        return new JsonObject { ["answer"] = answer.ToString(), ["events"] = events, ["trace"] = trace };
    }

    private static JsonNode Slim(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (value is JsonArray array && key == "embedding")
                    {
                        obj[key] = $"[{array.Count} numbers]";
                    }
                    else if (value is JsonArray chunks && key == "chunks")
                    {
                        obj[key] = new JsonObject { ["count"] = chunks.Count };
                    }
                    else if (value is not null)
                    {
                        Slim(value);
                    }
                }

                break;
            case JsonArray list:
                foreach (var item in list)
                {
                    if (item is not null)
                    {
                        Slim(item);
                    }
                }

                break;
        }

        return node;
    }

    private static JsonNode? Replace(JsonNode? node, Func<string, string> clean) => node switch
    {
        null => null,
        JsonObject obj => new JsonObject(obj.Select(pair => KeyValuePair.Create(pair.Key, Replace(pair.Value, clean)))),
        JsonArray list => new JsonArray(list.Select(item => Replace(item, clean)).ToArray()),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(clean(text)),
        _ => node.DeepClone()
    };

    /// <summary>
    /// Replaces each run copied from withheld text. Only letters and digits are compared, without
    /// case, so a copy with changed spacing, punctuation or capitals is still found.
    /// </summary>
    private static string WithholdQuotes(string text, Dictionary<string, (Withheld Source, int Index)> quotes)
    {
        if (quotes.Count == 0)
        {
            return text;
        }

        var (letters, positions) = Withheld.LettersOf(text);
        var result = new StringBuilder();
        var copied = 0;
        for (var i = 0; i + QuoteCharacters <= letters.Length;)
        {
            if (!quotes.TryGetValue(letters.Substring(i, QuoteCharacters), out var hit))
            {
                i++;
                continue;
            }

            var length = QuoteCharacters;
            while (i + length < letters.Length
                && hit.Index + length < hit.Source.Letters.Length
                && letters[i + length] == hit.Source.Letters[hit.Index + length])
            {
                length++;
            }

            var start = positions[i];
            var end = positions[i + length - 1] + 1;
            result.Append(text, copied, start - copied).Append($"[quotes {hit.Source.Name}, {end - start} characters]");
            copied = end;
            i += length;
        }

        return result.Append(text, copied, text.Length - copied).ToString();
    }

    private sealed record Withheld(string Name, string Text)
    {
        public string Letters { get; } = LettersOf(Text).Letters;

        /// <summary>The letters and digits of a text in lower case, and where each one sits in the original.</summary>
        public static (string Letters, List<int> Positions) LettersOf(string text)
        {
            var letters = new StringBuilder(text.Length);
            var positions = new List<int>(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsLetterOrDigit(text[i]))
                {
                    letters.Append(char.ToLowerInvariant(text[i]));
                    positions.Add(i);
                }
            }

            return (letters.ToString(), positions);
        }
    }
}
