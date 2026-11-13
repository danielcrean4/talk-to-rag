using System.Text.Json.Nodes;
using TalkToRag.Infrastructure.Audit;

namespace TalkToRag.UnitTests;

public class AuditJsonTests
{
    [Fact]
    public void ToNode_DropsEmbeddingVectorsAndIngestedChunks_ButKeepsTheRest()
    {
        var node = AuditJson.ToNode("""{"queries":[{"text":"td learning","embedding":[0.1,0.2,0.3]}],"chunks":[{"content":"a"},{"content":"b"}]}""");

        Assert.Equal("""{"queries":[{"text":"td learning","embedding":"[3 numbers]"}],"chunks":{"count":2}}""", AuditJson.Serialize(node));
    }

    [Fact]
    public void ToNode_RecordsOnlyTheSizeOfVeryLargeBodies_AndWrapsNonJson()
    {
        var huge = "\"" + new string('x', AuditJson.MaxBodyBytes) + "\"";

        Assert.True(AuditJson.ToNode(huge)["omitted"]!.GetValue<bool>());
        Assert.Equal("Internal Server Error", AuditJson.ToNode("Internal Server Error")["text"]!.GetValue<string>());
        Assert.Equal("{}", AuditJson.Serialize(AuditJson.ToNode(null)));
    }

    [Fact]
    public void ToNode_JoinsStreamedTokensIntoOneAnswer_AndLiftsTheTraceToTheTop()
    {
        var stream = "data: {\"token\": \"TD \"}\n\ndata: {\"token\": \"learning [1].\"}\n\ndata: {\"trace\": [{\"model\": \"local-small\"}]}\n\ndata: {\"done\": true, \"cited\": [1]}\n\n";

        var node = AuditJson.ToNode(stream, isEventStream: true);

        Assert.Equal("TD learning [1].", node["answer"]!.GetValue<string>());
        Assert.Single(node["events"]!.AsArray());
        Assert.Equal("local-small", node["trace"]![0]!["model"]!.GetValue<string>());
    }

    [Fact]
    public void Redact_ReplacesPassageTextEverywhere_IncludingInsideThePrompt_AndLeavesTheOriginalsAlone()
    {
        var request = AuditJson.ToNode("""{"question":"What is TD?","passages":[{"n":1,"content":"TD learning bootstraps."},{"n":2,"content":"Monte Carlo waits for the return."}]}""");
        var response = AuditJson.ToNode("""{"answer":"It bootstraps [1].","trace":[{"messages":[{"role":"user","content":"Reference Passages:\n[1] TD learning bootstraps.\n[2] Monte Carlo waits for the return.\n\nQuestion: What is TD?"}],"duration_ms":120}]}""");

        var (redactedRequest, redactedResponse) = AuditJson.Redact(request, response);

        var redacted = AuditJson.Serialize(redactedRequest) + AuditJson.Serialize(redactedResponse);
        Assert.DoesNotContain("bootstraps.", redacted);
        Assert.DoesNotContain("Monte Carlo", redacted);
        Assert.Contains("[1] [passage 1 withheld, 23 characters]", redacted);
        Assert.Contains("What is TD?", redacted);
        Assert.Contains("It bootstraps [1].", redacted);
        Assert.Equal(120, redactedResponse["trace"]![0]!["duration_ms"]!.GetValue<int>());
        Assert.Contains("TD learning bootstraps.", AuditJson.Serialize(request));
        Assert.Contains("Monte Carlo waits", AuditJson.Serialize(response));
    }

    [Fact]
    public void Redact_WithholdsTextSentToBeReadAloud_ByLength()
    {
        var request = AuditJson.ToNode("""{"text":"TD learning bootstraps from its own estimates [1].","voice":null,"blob_key":"audio/u/a.wav"}""");
        var response = AuditJson.ToNode("""{"blob_key":"audio/u/a.wav","format":"wav","content_type":"audio/wav","duration_seconds":2.5}""");

        var (redactedRequest, _) = AuditJson.Redact(request, response);

        Assert.Equal("[text to speak withheld, 50 characters]", redactedRequest["text"]!.GetValue<string>());
        Assert.Equal("audio/u/a.wav", redactedRequest["blob_key"]!.GetValue<string>());
    }

    [Fact]
    public void Redact_WithholdsRunsTheModelCopiedFromAPassage_HoweverSpacingPunctuationOrCaseChanged()
    {
        var request = AuditJson.ToNode("""{"passages":[{"n":3,"text":"Max pooling keeps the largest value in each window, which makes the output\nless sensitive to small shifts of the input."}]}""");
        var response = AuditJson.ToNode("""{"answer":"Pooling shrinks the map [3]. MAX POOLING keeps thelargestvalue in each window which makes the output less sensitive to small shifts. It keeps the largest value [3]."}""");

        var (_, redacted) = AuditJson.Redact(request, response);

        Assert.Equal(
            "Pooling shrinks the map [3]. [quotes passage 3, 102 characters]. It keeps the largest value [3].",
            redacted["answer"]!.GetValue<string>());
    }

    [Fact]
    public void Redact_WithholdsEarlierAnswersSentAsContext_ButKeepsWhatTheUserAsked()
    {
        var earlier = "Passage [1]: Temporal-difference learning updates estimates from other learned estimates.";
        var request = AuditJson.ToNode($$"""{"question":"and Monte Carlo?","recent_messages":[{"role":"user","content":"what is TD learning"},{"role":"assistant","content":"{{earlier}}"}]}""");
        var response = AuditJson.ToNode($$"""{"queries":[{"text":"Monte Carlo methods"}],"trace":[{"messages":[{"role":"user","content":"what is TD learning"},{"role":"assistant","content":"{{earlier}}"}],"completion":"Monte Carlo methods"}]}""");

        var (redactedRequest, redactedResponse) = AuditJson.Redact(request, response);

        var redacted = AuditJson.Serialize(redactedRequest) + AuditJson.Serialize(redactedResponse);
        Assert.DoesNotContain("other learned estimates", redacted);
        Assert.Contains("[earlier answer withheld, 89 characters]", redacted);
        Assert.Contains("what is TD learning", redacted);
        Assert.Contains("and Monte Carlo?", redacted);
    }

    [Fact]
    public void Redact_HandlesRequestsWithoutPassages_AndMissingResponses()
    {
        var (request, response) = AuditJson.Redact(JsonNode.Parse("""{"concept":"Pooling"}""")!, null);

        Assert.Equal("""{"concept":"Pooling"}""", AuditJson.Serialize(request));
        Assert.Equal("{}", AuditJson.Serialize(response));
    }
}
