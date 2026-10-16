using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Moq;
using System.Net;
using System.Net.Http.Json;
using TaskAPI;
using Xunit;
using Microsoft.Extensions.DependencyInjection.Extensions;

public class CompleteTaskIntegrationTest : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<IRabbitMqService> _rabbitMock = new();

    public CompleteTaskIntegrationTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Подменяем RabbitMQ на мок
                services.RemoveAll<IRabbitMqService>();
                services.AddSingleton(_rabbitMock.Object);
            });
        });
    }

    [Fact]
    public async Task Create_Then_Complete_Publishes_RabbitMQ_Message()
    {
        // Arrange
        var client = _factory.CreateClient();

        // 1. Создать задачу
        var createResp = await client.PostAsJsonAsync("/tasks", new { title = "Buy milk" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var created = await createResp.Content.ReadFromJsonAsync<TaskDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.False(created!.IsCompleted);
        Assert.Null(created.CompletedAt);

        // 2. Завершить её
        var completeResp = await client.PutAsync($"/tasks/{created.Id}/complete", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);

        var completed = await completeResp.Content.ReadFromJsonAsync<TaskDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(completed);
        Assert.True(completed!.IsCompleted);
        Assert.NotNull(completed.CompletedAt);

        // 3. Проверить, что RabbitMQ получил сообщение
        // Даём немного времени на fire-and-forget публикацию
        await Task.Delay(500, TestContext.Current.CancellationToken);

        _rabbitMock.Verify(r => r.PublishTaskCompletedAsync(
            It.Is<TaskCompletedMessage>(m =>
                m.TaskId == created.Id &&
                m.Title == "Buy milk" &&
                m.Priority == "Medium"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}