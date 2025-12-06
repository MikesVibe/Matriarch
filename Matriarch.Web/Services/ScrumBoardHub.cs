using Microsoft.AspNetCore.SignalR;

namespace Matriarch.Web.Services;

public class ScrumBoardHub : Hub
{
    public async Task TaskAdded(string taskJson)
    {
        await Clients.All.SendAsync("TaskAdded", taskJson);
    }

    public async Task TaskMoved(string taskId, string newStatus)
    {
        await Clients.All.SendAsync("TaskMoved", taskId, newStatus);
    }

    public async Task TaskDeleted(string taskId)
    {
        await Clients.All.SendAsync("TaskDeleted", taskId);
    }
}
