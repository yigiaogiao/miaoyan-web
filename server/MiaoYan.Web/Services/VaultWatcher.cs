using System.Collections.Concurrent;

namespace MiaoYan.Web.Services;

/// <summary>
/// Watches each resolved vault for external edits. Events are debounced (editors
/// write in bursts) and translated into SSE changes after invalidating the
/// in-memory scan.
/// </summary>
public sealed class VaultWatcher : IDisposable
{
    private readonly VaultService _vaults;
    private readonly VaultEventHub _hub;
    private readonly ILogger<VaultWatcher> _logger;
    private readonly ConcurrentDictionary<string, WatchState> _watchers = new(StringComparer.Ordinal);

    public VaultWatcher(VaultService vaults, VaultEventHub hub, ILogger<VaultWatcher> logger)
    {
        _vaults = vaults;
        _hub = hub;
        _logger = logger;
    }

    public void Ensure(VaultRuntime runtime)
    {
        _watchers.GetOrAdd(runtime.Key, _ => Start(runtime));
    }

    private WatchState Start(VaultRuntime runtime)
    {
        var state = new WatchState();
        try
        {
            var watcher = new FileSystemWatcher(runtime.Paths.VaultRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024,
            };

            watcher.Created += (_, e) => Schedule(runtime, state, e.FullPath, "created");
            watcher.Changed += (_, e) => Schedule(runtime, state, e.FullPath, "changed");
            watcher.Deleted += (_, e) => Schedule(runtime, state, e.FullPath, "deleted");
            watcher.Renamed += (_, e) =>
            {
                Schedule(runtime, state, e.OldFullPath, "deleted");
                Schedule(runtime, state, e.FullPath, "changed");
            };
            watcher.Error += (_, e) =>
            {
                _logger.LogWarning(e.GetException(), "Vault watcher error for {Key}", runtime.Key);
                ScheduleInvalidate(runtime, state);
            };

            state.Watcher = watcher;
            watcher.EnableRaisingEvents = true;
            _logger.LogInformation("Watching vault '{Key}' at {Root}", runtime.Key, runtime.Paths.VaultRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Cannot watch vault '{Key}'; falling back to manual refresh", runtime.Key);
        }

        return state;
    }

    private void Schedule(VaultRuntime runtime, WatchState state, string fullPath, string kind)
    {
        if (IsIgnored(runtime, fullPath) || _hub.IsSuppressed(runtime.Key, fullPath))
        {
            return;
        }

        var pending = state.Pending;
        if (pending.TryRemove(fullPath, out var oldTimer))
        {
            oldTimer.Dispose();
        }

        var timer = new Timer(_ =>
        {
            if (pending.TryRemove(fullPath, out var finished))
            {
                finished.Dispose();
            }

            _vaults.Invalidate(runtime.Key);
            var relative = TryRelative(runtime, fullPath);
            if (relative is null)
            {
                return;
            }

            var noteId = relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? VaultService.NoteIdFor(relative)
                : null;
            _hub.Publish(runtime.Key, new VaultChange(kind, noteId, relative, DateTimeOffset.UtcNow));
        }, null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);

        pending[fullPath] = timer;
    }

    private void ScheduleInvalidate(VaultRuntime runtime, WatchState state)
    {
        var timer = new Timer(_ =>
        {
            _vaults.Invalidate(runtime.Key);
            _hub.Publish(runtime.Key, new VaultChange("refresh", null, null, DateTimeOffset.UtcNow));
        }, null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);

        state.Pending[$"__refresh-{Guid.NewGuid():N}"] = timer;
    }

    private static bool IsIgnored(VaultRuntime runtime, string fullPath)
    {
        var relative = TryRelative(runtime, fullPath);
        if (relative is null)
        {
            return true;
        }

        return relative.Split('/').Any(segment =>
            segment.StartsWith('.')
            || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryRelative(VaultRuntime runtime, string fullPath)
    {
        try
        {
            return VaultPath.IsInside(runtime.Paths.VaultRoot, fullPath)
                ? VaultPath.Relative(runtime.Paths.VaultRoot, fullPath)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var state in _watchers.Values)
        {
            state.Watcher?.Dispose();
            foreach (var timer in state.Pending.Values)
            {
                timer.Dispose();
            }
        }

        _watchers.Clear();
    }

    private sealed class WatchState
    {
        public FileSystemWatcher? Watcher { get; set; }
        public ConcurrentDictionary<string, Timer> Pending { get; } = new(StringComparer.Ordinal);
    }
}
