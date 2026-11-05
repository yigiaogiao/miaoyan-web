using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MiaoYan.Web.Services;

public sealed record VaultChange(string Kind, string? NoteId, string? Path, DateTimeOffset At);

/// <summary>
/// Fan-out for vault changes. Each open browser tab has one SSE subscription;
/// writes performed by this process are suppressed for the watcher so the tab
/// that saved does not receive its own echo twice.
/// </summary>
public sealed class VaultEventHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, Channel<VaultChange>>> _subscribers = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _suppressed = new();
    private long _nextId;

    public (long Id, ChannelReader<VaultChange> Reader) Subscribe(string vaultKey)
    {
        var id = Interlocked.Increment(ref _nextId);
        var channel = Channel.CreateBounded<VaultChange>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var subscribers = _subscribers.GetOrAdd(vaultKey, _ => new ConcurrentDictionary<long, Channel<VaultChange>>());
        subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(string vaultKey, long id)
    {
        if (_subscribers.TryGetValue(vaultKey, out var subscribers))
        {
            subscribers.TryRemove(id, out _);
            if (subscribers.IsEmpty)
            {
                _subscribers.TryRemove(vaultKey, out _);
            }
        }
    }

    public void Publish(string vaultKey, VaultChange change)
    {
        if (!_subscribers.TryGetValue(vaultKey, out var subscribers))
        {
            return;
        }

        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryWrite(change);
        }
    }

    public void Suppress(string vaultKey, string fullPath)
    {
        var paths = _suppressed.GetOrAdd(vaultKey, _ => new ConcurrentDictionary<string, long>(StringComparer.Ordinal));
        paths[fullPath] = DateTime.UtcNow.Ticks;
    }

    public bool IsSuppressed(string vaultKey, string fullPath)
    {
        if (!_suppressed.TryGetValue(vaultKey, out var paths)
            || !paths.TryGetValue(fullPath, out var ticks))
        {
            return false;
        }

        if (DateTime.UtcNow.Ticks - ticks < TimeSpan.FromSeconds(1).Ticks)
        {
            return true;
        }

        paths.TryRemove(fullPath, out _);
        return false;
    }
}
