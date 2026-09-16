using System.Net;
using System.Text;

namespace ValidationPlatform.Tests.LaaSmoke;

internal sealed class JournalResponseHandler(string wire) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(wire, Encoding.UTF8, "application/json")
        });
    }
}
