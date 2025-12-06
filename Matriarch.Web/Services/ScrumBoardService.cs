using Matriarch.Web.Models;
using System.Collections.Concurrent;

namespace Matriarch.Web.Services;

public interface IScrumBoardService
{
    IEnumerable<ScrumTask> GetAllTasks();
    ScrumTask? GetTask(string id);
    void AddTask(ScrumTask task);
    void MoveTask(string id, ScrumTaskStatus newStatus);
    void DeleteTask(string id);
}

public class ScrumBoardService : IScrumBoardService
{
    private readonly ConcurrentDictionary<string, ScrumTask> _tasks = new();

    public IEnumerable<ScrumTask> GetAllTasks()
    {
        return _tasks.Values.OrderBy(t => t.CreatedAt);
    }

    public ScrumTask? GetTask(string id)
    {
        _tasks.TryGetValue(id, out var task);
        return task;
    }

    public void AddTask(ScrumTask task)
    {
        _tasks[task.Id] = task;
    }

    public void MoveTask(string id, ScrumTaskStatus newStatus)
    {
        if (_tasks.TryGetValue(id, out var task))
        {
            task.Status = newStatus;
            task.UpdatedAt = DateTime.UtcNow;
        }
    }

    public void DeleteTask(string id)
    {
        _tasks.TryRemove(id, out _);
    }
}
