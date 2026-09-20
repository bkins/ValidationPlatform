using System.Net;
using System.Text;
using LocalAIAssistant.CognitivePlatform.CpClients.Tasks;
using LocalAIAssistant.Knowledge.Tasks.Models;

namespace ValidationPlatform.Tests.LaaSmoke;

[Trait("Category", "ClientContract")]
public class TaskClientContractTests
{
    [Fact]
    public async Task GetByIdAsync_ApiStringPriority_DeserializesWithoutCrashing()
    {
        var id = Guid.NewGuid();
        var json = $$"""
                     {
                       "id": "{{id:N}}",
                       "shortDescription": "go to store tomorrow",
                       "details": null,
                       "priority": "Normal",
                       "isImportant": true,
                       "isUrgent": false,
                       "createdAt": "2026-09-20T11:01:18.7862319-07:00",
                       "updatedAt": "2026-09-20T11:01:18.7863569-07:00",
                       "dueDate": null,
                       "completedAt": null,
                       "tags": []
                     }
                     """;
        var handler = new JsonResponseHandler(json);
        var client  = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var sut     = new TaskApiClient(client);

        var result = await sut.GetByIdAsync(id);

        Assert.NotNull(result);
        Assert.Equal(TaskPriorityDto.Normal, result!.Priority);
        Assert.Equal("go to store tomorrow", result.ShortDescription);
    }

    private sealed class JsonResponseHandler : HttpMessageHandler
    {
        private readonly string _json;

        public JsonResponseHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage  request
                                                              , CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                                   {
                                       Content = new StringContent(_json, Encoding.UTF8, "application/json")
                                   });
        }
    }
}
