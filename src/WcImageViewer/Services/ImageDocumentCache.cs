using WcImageViewer.Models;

namespace WcImageViewer.Services;

/// <summary>Keeps the current image and its neighbors decoded.</summary>
public sealed class ImageDocumentCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _lru = new();
    private readonly object _gate = new();

    public ImageDocumentCache(int capacity = 3)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool TryGet(string key, out ImageDocument? document)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                document = null;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            document = node.Value.Document;
            return true;
        }
    }

    public void Set(string key, ImageDocument document)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value.Document.Dispose();
                existing.Value = new Entry(key, document);
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            var node = _lru.AddFirst(new Entry(key, document));
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
                last.Value.Document.Dispose();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var entry in _lru)
            {
                entry.Document.Dispose();
            }

            _map.Clear();
            _lru.Clear();
        }
    }

    private readonly record struct Entry(string Key, ImageDocument Document);
}
