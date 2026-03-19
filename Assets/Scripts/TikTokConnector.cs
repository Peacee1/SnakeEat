using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Connects to the Node.js TikTok bridge via WebSocket.
/// Raise events that GameManager subscribes to.
/// </summary>
public class TikTokConnector : MonoBehaviour
{
    [Header("Bridge Settings")]
    [SerializeField] private string serverUrl = "ws://localhost:8765";
    [SerializeField] private float  reconnectDelay = 3f;

    // Raised on the main thread
    public event Action<GiftEvent> OnGift;
    public event Action<LikeEvent> OnLike;
    public event Action<string>    OnConnected;
    public event Action            OnDisconnected;

    private ClientWebSocket    _ws;
    private CancellationTokenSource _cts;
    private readonly System.Collections.Generic.Queue<string> _mainThreadQueue = new();
    private readonly object _lock = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Start() => StartConnection();

    private void Update()
    {
        lock (_lock)
        {
            while (_mainThreadQueue.Count   > 0) HandleMessage(_mainThreadQueue.Dequeue());
            while (_mainThreadActions.Count > 0) _mainThreadActions.Dequeue()?.Invoke();
        }
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }

    // ── Connection ─────────────────────────────────────────────────────────────

    private async void StartConnection()
    {
        while (true)
        {
            _cts = new CancellationTokenSource();
            _ws  = new ClientWebSocket();
            try
            {
                Debug.Log($"[TikTok] Connecting to {serverUrl}…");
                await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);
                Debug.Log("[TikTok] Connected ✓");
                Enqueue(() => OnConnected?.Invoke(serverUrl));
                await ReceiveLoop();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Debug.LogWarning($"[TikTok] Connection error: {ex.Message}");
            }
            finally
            {
                _ws?.Dispose();
                Enqueue(() => OnDisconnected?.Invoke());
            }

            if (_cts.IsCancellationRequested) break;
            Debug.Log($"[TikTok] Reconnecting in {reconnectDelay}s…");
            await Task.Delay(TimeSpan.FromSeconds(reconnectDelay), CancellationToken.None);
        }
    }

    private async Task ReceiveLoop()
    {
        var buf = new byte[4096];
        while (_ws.State == WebSocketState.Open)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string json = sb.ToString();
                lock (_lock) _mainThreadQueue.Enqueue(json);
            }
        }
    }

    // ── Message handling ───────────────────────────────────────────────────────

    private void HandleMessage(string json)
    {
        try
        {
            var raw = JsonUtility.FromJson<RawEvent>(json);
            if (raw.type == "gift")
            {
                var e = JsonUtility.FromJson<GiftEvent>(json);
                Debug.Log($"[TikTok] 🎁 Gift from @{e.username}: {e.giftName} x{e.repeatCount} ({e.diamonds * e.repeatCount}💎)");
                OnGift?.Invoke(e);
            }
            else if (raw.type == "like")
            {
                var e = JsonUtility.FromJson<LikeEvent>(json);
                OnLike?.Invoke(e);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TikTok] Parse error: {ex.Message} | json={json}");
        }
    }

    private readonly System.Collections.Generic.Queue<Action> _mainThreadActions = new();

    private void Enqueue(Action action)
    {
        // Queue the action to be dispatched on the main thread in Update().
        lock (_lock) _mainThreadActions.Enqueue(action);
    }

    // ── Data classes ───────────────────────────────────────────────────────────

    [Serializable] private class RawEvent  { public string type; }

    [Serializable]
    public class GiftEvent
    {
        public string type;
        public string username;
        public int    giftId;
        public string giftName;
        public int    diamonds;
        public int    repeatCount;
    }

    [Serializable]
    public class LikeEvent
    {
        public string type;
        public string username;
        public int    count;       // total likes so far in this session (from TikTok)
    }
}
