using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using System.Reflection;

namespace BetterMap.Core;

public partial class MapOverviewPanel : Control
{
    // ================== 基准参数 (以 1080P 为基准) ==================
    private const float BaseLeft = 100f;
    private const float BaseTop = 150f;
    private const float BaseWidth = 280f;
    private const float BaseBottomPad = 60f;
    private const float BaseInnerPad = 3f;
    private const float BgAlpha = 0.88f;

    // Timer 刷新频率
    private const double SyncInterval = 1d / 144;
    // 小地图专属的渲染层 (Bit 1, 值 2)
    private const uint MinimapLayerBit = 2u;
    // ===============================================================

    private ColorRect _background;
    private SubViewportContainer _svc;
    private SubViewport _sv;
    private ColorRect _viewportIndicator;
    private Godot.Timer _syncTimer;
    private bool _built;

    private Control _mapContainer;
    private NMapScreen _mapScreen;
    private Vector2 _worldMin;
    private Vector2 _worldMax;

    private Rid _svRid;
    private Rid _mapCanvasRid;
    private bool _canvasReady;
    private float _scale;
    private Vector2 _lastMapPosition = new Vector2(float.NaN, float.NaN);

    private static readonly FieldInfo DictField =
        typeof(NMapScreen).GetField("_mapPointDictionary",
            BindingFlags.NonPublic | BindingFlags.Instance);

    public static MapOverviewPanel Create() =>
        new() { Name = "BetterMapOverviewPanel", Visible = false };

    public void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        AnchorLeft = AnchorTop = AnchorRight = AnchorBottom = 0f;
        MouseFilter = MouseFilterEnum.Ignore;

        _background = new ColorRect
        {
            Name = "PanelBg",
            Color = new Color(0.05f, 0.04f, 0.03f, BgAlpha),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_background);

        _svc = new SubViewportContainer
        {
            Name = "SVC",
            Stretch = true,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_svc);

        _sv = new SubViewport
        {
            Name = "SV",
            TransparentBg = true,
            HandleInputLocally = false,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,

            CanvasCullMask = MinimapLayerBit,
        };
        _svc.AddChild(_sv);

        _viewportIndicator = new ColorRect
        {
            Name = "ViewportIndicator",
            Color = new Color(1f, 1f, 1f, 0.18f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _svc.AddChild(_viewportIndicator);

        _syncTimer = new Godot.Timer
        {
            Name = "MapSyncTimer",
            WaitTime = SyncInterval,
            OneShot = false,
            Autostart = false
        };
        _syncTimer.Timeout += OnSyncTimerTimeout;
        AddChild(_syncTimer);
    }

    public override void _Ready() { EnsureBuilt(); ApplyLayout(); }

    public override void _ExitTree() { TeardownCanvas(); }

    public void BuildOverview(NMapScreen screen)
    {
        EnsureBuilt();
        _mapScreen = screen;
        _mapContainer = screen.GetNodeOrNull<Control>("TheMap");
        if (_mapContainer == null) return;
        ComputeWorldBounds(screen);
    }

    public void ShowPanel()
    {
        EnsureBuilt();
        Visible = true;
        _lastMapPosition = new Vector2(float.NaN, float.NaN);
        ApplyLayout();
        SetupCanvas();

        // 开启通道：赋予地图本身以及所有父节点第 2 层可见性
        EnableMinimapVisibility();

        _syncTimer.Start();
    }

    public void HidePanel()
    {
        Visible = false;
        TeardownCanvas();
    }

    private void SetupCanvas()
    {
        if (_canvasReady || _mapContainer == null || _mapScreen == null) return;

        try
        {
            _svRid = _sv.GetViewportRid();
            _mapCanvasRid = _mapContainer.GetCanvas();

            if (!_svRid.IsValid || !_mapCanvasRid.IsValid) return;

            RenderingServer.ViewportAttachCanvas(_svRid, _mapCanvasRid);

            var svSize = new Vector2(_sv.Size.X, _sv.Size.Y);
            var mapRange = _worldMax - _worldMin;
            _scale = Mathf.Min(svSize.X / mapRange.X, svSize.Y / mapRange.Y);

            _canvasReady = true;
            ForceSyncTransform();
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"SetupCanvas 异常: {ex}");
        }
    }

    private void TeardownCanvas()
    {
        if (_syncTimer != null) _syncTimer.Stop();

        if (_svRid.IsValid && _mapCanvasRid.IsValid)
        {
            try { RenderingServer.ViewportRemoveCanvas(_svRid, _mapCanvasRid); }
            catch (System.Exception ex) { ModLogger.Warn($"RemoveCanvas: {ex.Message}"); }
        }
        _canvasReady = false;

        DisableMinimapVisibility();
    }

    // =========================================================================
    // 核心可见性逻辑（打通渲染通道）
    // =========================================================================

    private void EnableMinimapVisibility()
    {
        if (_mapContainer == null) return;

        SetVisibilityRecursive(_mapContainer, MinimapLayerBit, true);

        Node current = _mapContainer.GetParent();
        while (current != null && current is CanvasItem ci)
        {
            ci.VisibilityLayer |= MinimapLayerBit; // 添加标记，不破坏原有的第1层
            current = current.GetParent();
        }
    }

    private void DisableMinimapVisibility()
    {
        if (_mapContainer == null) return;

        // 撤销地图及其子节点的通行证
        SetVisibilityRecursive(_mapContainer, MinimapLayerBit, false);

        // 撤销祖先节点的通行证
        Node current = _mapContainer.GetParent();
        while (current != null && current is CanvasItem ci)
        {
            ci.VisibilityLayer &= ~MinimapLayerBit;
            current = current.GetParent();
        }
    }

    private static void SetVisibilityRecursive(Node node, uint bitMask, bool enable)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return;

        if (node is CanvasItem ci)
        {
            if (enable) ci.VisibilityLayer |= bitMask;
            else ci.VisibilityLayer &= ~bitMask;
        }

        int childCount = node.GetChildCount();
        for (int i = 0; i < childCount; i++)
        {
            SetVisibilityRecursive(node.GetChild(i), bitMask, enable);
        }
    }

    // =========================================================================
    // 同步与布局逻辑
    // =========================================================================

    private void OnSyncTimerTimeout()
    {
        if (!Visible || !_canvasReady) return;
        if (_mapContainer == null || !GodotObject.IsInstanceValid(_mapContainer)) return;

        var currentPos = _mapContainer.GlobalPosition;
        if (currentPos.IsEqualApprox(_lastMapPosition)) return;

        _lastMapPosition = currentPos;
        ForceSyncTransform();
        UpdateViewportIndicator();
    }

    private void ForceSyncTransform()
    {
        var svSize = new Vector2(_sv.Size.X, _sv.Size.Y);
        var mapRange = _worldMax - _worldMin;
        float offsetX = (svSize.X - mapRange.X * _scale) * 0.5f;
        float offsetY = (svSize.Y - mapRange.Y * _scale) * 0.5f;

        var mp = _mapContainer.GlobalPosition;
        var t = new Transform2D(
            new Vector2(_scale, 0),
            new Vector2(0, _scale),
            new Vector2(-(mp.X + _worldMin.X) * _scale + offsetX,
                        -(mp.Y + _worldMin.Y) * _scale + offsetY)
        );
        RenderingServer.ViewportSetCanvasTransform(_svRid, _mapCanvasRid, t);
    }

    private void ApplyLayout()
    {
        if (_background == null || _svc == null) return;
        var vp = GetViewport();
        if (vp == null) return;

        Vector2 screenSize = vp.GetVisibleRect().Size;
        float gs = screenSize.Y / 1080f;

        float left = BaseLeft * gs;
        float top = BaseTop * gs;
        float width = BaseWidth * gs;
        float botPad = BaseBottomPad * gs;
        float innerPad = BaseInnerPad * gs;

        var mapRange = _worldMax - _worldMin;
        if (mapRange.X < 1f || mapRange.Y < 1f) return;

        float maxH = screenSize.Y - top - botPad;
        float innerW = width - innerPad * 2f;
        float innerH = Mathf.Min(innerW * (mapRange.Y / mapRange.X), maxH - innerPad * 2f);
        float panelH = innerH + innerPad * 2f;

        Position = new Vector2(left, top);
        Size = new Vector2(width, panelH);
        _background.Size = Size;
        _svc.Position = new Vector2(innerPad, innerPad);
        _svc.Size = new Vector2(innerW, innerH);
        _sv.Size = (Vector2I)_svc.Size;
        _scale = Mathf.Min(innerW / mapRange.X, innerH / mapRange.Y);
    }

    private void UpdateViewportIndicator()
    {
        if (_viewportIndicator == null || _mapScreen == null || _mapContainer == null) return;

        var svcSize = _svc.Size;
        var mapRange = _worldMax - _worldMin;
        float offX = (svcSize.X - mapRange.X * _scale) * 0.5f;
        float offY = (svcSize.Y - mapRange.Y * _scale) * 0.5f;

        float visTop = -_mapContainer.GlobalPosition.Y;
        float top = (visTop - _worldMin.Y) * _scale + offY;
        float w = mapRange.X * _scale;
        float h = _mapScreen.Size.Y * _scale;

        float drawTop = Mathf.Max(top, 0);
        float drawBot = Mathf.Min(top + h, svcSize.Y);
        float drawH = Mathf.Max(drawBot - drawTop, 0);

        _viewportIndicator.Position = new Vector2(offX, drawTop);
        _viewportIndicator.Size = new Vector2(w, drawH);
        _viewportIndicator.Visible = drawH > 0;
    }

    private void ComputeWorldBounds(NMapScreen screen)
    {
        _worldMin = new Vector2(float.MaxValue, float.MaxValue);
        _worldMax = new Vector2(float.MinValue, float.MinValue);

        var dict = DictField?.GetValue(screen) as Dictionary<MapCoord, NMapPoint>;
        if (dict == null || dict.Count == 0)
        {
            _worldMin = new Vector2(-600f, -2400f);
            _worldMax = new Vector2(600f, 800f);
            return;
        }

        foreach (var kv in dict)
        {
            var p = kv.Value.Position;
            if (p.X < _worldMin.X) _worldMin.X = p.X;
            if (p.Y < _worldMin.Y) _worldMin.Y = p.Y;
            if (p.X > _worldMax.X) _worldMax.X = p.X;
            if (p.Y > _worldMax.Y) _worldMax.Y = p.Y;
        }

        _worldMin -= new Vector2(80f, 100f);
        _worldMax += new Vector2(80f, 100f);
    }
}