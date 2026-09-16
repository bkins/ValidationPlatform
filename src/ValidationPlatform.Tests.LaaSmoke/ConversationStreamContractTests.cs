using CognitivePlatform.Api.Contracts;
using LocalAIAssistant.CognitivePlatform.CpClients.CognitivePlatform;

namespace ValidationPlatform.Tests.LaaSmoke;

public class ConversationStreamContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void NegotiatedStream_ApiFramesAndClientDecoder_PreserveMultilineResponse()
    {
        var chunks = new[] { "Results for 'joe':\r\n\r\n[journal] entry #1\n  Synthetic memory.", "\n\n", "Next", " token", "", "\"quote\" \\ 😀" };
        var wire = string.Concat(chunks.Select(chunk => ConversationStreamFrame.Encode(chunk, true)));

        var decoded = wire.Split('\n').Where(line => line.StartsWith("data: "))
                          .Select(line => ConversationStreamPayload.Decode(line[6..], true)).ToArray();

        Assert.Equal(chunks, decoded);
        Assert.Equal(string.Concat(chunks), string.Concat(decoded));
        Assert.Equal(ConversationStreamFrame.HeaderName, ConversationStreamPayload.HeaderName);
        Assert.Equal(ConversationStreamFrame.JsonStringFormat, ConversationStreamPayload.JsonStringFormat);
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public void LegacyStream_UnnegotiatedSingleLineChunks_RetainsCompatibility()
    {
        var frame = ConversationStreamFrame.Encode(" ordinary token ", false);

        var decoded = ConversationStreamPayload.Decode(frame.Split('\n')[0][6..], false);

        Assert.Equal(" ordinary token ", decoded);
    }
}
