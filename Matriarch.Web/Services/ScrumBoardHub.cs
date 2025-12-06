using Microsoft.AspNetCore.SignalR;
using Matriarch.Web.Models;
using System.Text.Json;

namespace Matriarch.Web.Services;

public class ScrumBoardHub : Hub
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task TaskAdded(string taskJson)
    {
        // Validate the task JSON before broadcasting
        try
        {
            var task = JsonSerializer.Deserialize<ScrumTask>(taskJson, JsonOptions);
            if (task != null && !string.IsNullOrWhiteSpace(task.Title))
            {
                await Clients.Others.SendAsync("TaskAdded", taskJson);
            }
        }
        catch (JsonException)
        {
            // Invalid JSON, don't broadcast
        }
    }

    public async Task TaskMoved(string taskId, string newStatus)
    {
        // Validate inputs before broadcasting
        if (!string.IsNullOrWhiteSpace(taskId) && 
            Enum.TryParse<ScrumTaskStatus>(newStatus, out _))
        {
            await Clients.Others.SendAsync("TaskMoved", taskId, newStatus);
        }
    }

    public async Task TaskDeleted(string taskId)
    {
        // Validate input before broadcasting
        if (!string.IsNullOrWhiteSpace(taskId))
        {
            await Clients.Others.SendAsync("TaskDeleted", taskId);
        }
    }
}
