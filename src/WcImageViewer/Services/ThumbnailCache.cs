using System.Windows.Media.Imaging;

namespace WcImageViewer.Services;

/// <summary>Small LRU cache for decoded thumbnails.</summary>
public sealed class ThumbnailCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _lru = new();
    private readonly object _gate = new();

    public ThumbnailCache(int capacity = 400)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool TryGet(string key, out BitmapSource? value)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                value = null;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            value = node.Value.Image;
            return true;
        }
    }

    public void Set(string key, BitmapSource image)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value = new Entry(key, image);
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            var node = _lru.AddFirst(new Entry(key, image));
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var last = _lru.Last;
                if (last is null)
                {
                    break;
                }

                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    private readonly record struct Entry(string Key, BitmapSource Image);
}
