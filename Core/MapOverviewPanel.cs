using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace BetterMap.Core;

public partial class MapOverviewPanel : Control
{
    // ================== 基准参数 (以 1080P 为基准) ==================
    private const float BaseLeft = 100f;
    private const float BaseTop = 150f;
    private const float BaseWidth = 280f;
    private const float BaseBottomPad = 60f;
    private const float BaseInnerPad = 6f;
    private const float BgAlpha = 0.88f;
    // ===============================================================

    // layer=1 → TheMap，CullMask bit=0
    // layer=2 → 所有需要在TheMap之上的节点
    private const int LayerMap = 1;
    private const int LayerAbove = 2;
    private const uint SvCullMask = 1u << (LayerMap - 1); // = 1u

    // NMapScreen 内需要提升的节点
    private static readonly string[] MapScreenAboveNames =
        { "MapLegend", "Back", "DrawingTools", "DrawingToolsHotkey", "ActBanner" };

    // NGlobalUi 内需要提升的节点（排在 NMapScreen 之后、需要显示在地图之上的）
    // 根据之前日志：index 0=OverlayScreensContainer 在 NMapScreen 之前，index 2以后在之后
    // 全部提升到 layer=2 保险
    private static readonly string[] GlobalUiAboveNames =
    {
        "OverlayScreensContainer",
        "CapstoneScreenContainer",
        "MultiplayerPlayerContainer",
        "RelicInventory",
        "MultiplayerTimeoutOverlay",
        "TopBar",
        "AboveTopBarVfxContainer",
        "CardPreviewContainer",
        "GridCardPreviewContainer",
        "EventCardPreviewContainer",
        "MessyCardPreviewContainer",
        "FpsVisualizer",
        "ParticleCounter",
        "DebugInfo",
        "TargetManager",
    };

    private ColorRect _background;
    private SubViewportContainer _svc;
    private SubViewport _sv;
    private ColorRect _viewportIndicator;
    private bool _built;

    private Control _mapContainer;
    private NMapScreen _mapScreen;
    private Vector2 _worldMin;
    private Vector2 _worldMax;

    private Rid _svRid;
    private Rid _mapCanvasRid;
    private bool _canvasReady;
    private float _scale;

    private CanvasLayer _clMap;    // layer=1，挂在 NMapScreen 下
    private CanvasLayer _clAbove;  // layer=2，挂在 NGlobalUi 下，容纳所有上层节点

    private Node _mapOrigParent;
    private int _mapOrigIndex;

    // 还原列表：所有被移动的节点及其原始位置
    private readonly List<(Node node, Node origParent, int origIndex)> _movedNodes = new();

    private static readonly FieldInfo DictField =
        typeof(NMapScreen).GetField("_mapPointDictionary",
            BindingFlags.NonPublic | BindingFlags.Instance);

    // ──────────────────────────────────────────────────────────────
    public static MapOverviewPanel Create() =>
        new() { Name = "BetterMapOverviewPanel", Visible = false };

    public void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        AnchorLeft = AnchorTop = AnchorRight = AnchorBottom = 0f;
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;

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
            CanvasCullMask = SvCullMask,
        };
        _svc.AddChild(_sv);

        _viewportIndicator = new ColorRect
        {
            Name = "ViewportIndicator",
            Color = new Color(1f, 1f, 1f, 0.18f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 10,
        };
        _svc.AddChild(_viewportIndicator);

        RenderingServer.FramePostDraw += OnFramePostDraw;
        ModLogger.Info($"EnsureBuilt 完成 SvCullMask={SvCullMask}");
    }

    public override void _Ready() { EnsureBuilt(); ApplyLayout(); }
    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= OnFramePostDraw;
        TeardownCanvas();
    }
    public override void _Process(double delta) { }

    // ──────────────────────────────────────────────────────────────
    public void BuildOverview(NMapScreen screen)
    {
        EnsureBuilt();
        _mapScreen = screen;
        _mapContainer = screen.GetNodeOrNull<Control>("TheMap");
        if (_mapContainer == null) { ModLogger.Warn("BuildOverview: 找不到 TheMap"); return; }
        ComputeWorldBounds(screen);
        ModLogger.Info($"BuildOverview: worldMin={_worldMin} worldMax={_worldMax}");
    }

    public void ShowPanel()
    {
        EnsureBuilt();
        Visible = true;
        Callable.From(DeferredShow).CallDeferred();
    }

    public void HidePanel()
    {
        Visible = false;
        TeardownCanvas();
    }

    // ──────────────────────────────────────────────────────────────
    private void DeferredShow()
    {
        ApplyLayout();
        SetupCanvas();
    }

    private void SetupCanvas()
    {
        if (_canvasReady || _mapContainer == null || _mapScreen == null) return;

        try
        {
            var globalUi = _mapScreen.GetParent(); // NGlobalUi

            // ── 1. 创建 CanvasLayer 节点 ──
            // _clMap 挂在 NMapScreen 下（坐标系与 TheMap 一致）
            _clMap = new CanvasLayer { Name = "BM_LayerMap", Layer = LayerMap };
            _mapScreen.AddChild(_clMap);

            // _clAbove 挂在 NGlobalUi 下（覆盖整个 UI 层）
            _clAbove = new CanvasLayer { Name = "BM_LayerAbove", Layer = LayerAbove };
            globalUi.AddChild(_clAbove);

            // ── 2. TheMap → layer=1 ──
            _mapOrigParent = _mapContainer.GetParent();
            _mapOrigIndex = _mapContainer.GetIndex();
            _mapContainer.Reparent(_clMap, keepGlobalTransform: true);
            ModLogger.Info($"TheMap → layer={LayerMap}, canvas={_mapContainer.GetCanvas()}");

            // ── 3. NMapScreen 内上层UI → layer=2 ──
            _movedNodes.Clear();
            MoveNodesToAbove(_mapScreen, MapScreenAboveNames);

            // ── 4. NGlobalUi 内其他上层节点 → layer=2 ──
            MoveNodesToAbove(globalUi, GlobalUiAboveNames);

            // ── 5. 附加 Canvas 到 SubViewport ──
            _svRid = _sv.GetViewportRid();
            _mapCanvasRid = _mapContainer.GetCanvas();

            if (!_svRid.IsValid || !_mapCanvasRid.IsValid)
            {
                ModLogger.Warn("SetupCanvas: RID 无效");
                return;
            }

            RenderingServer.ViewportAttachCanvas(_svRid, _mapCanvasRid);

            var svSize = new Vector2(_sv.Size.X, _sv.Size.Y);
            var mapRange = _worldMax - _worldMin;
            _scale = Mathf.Min(svSize.X / mapRange.X, svSize.Y / mapRange.Y);

            _canvasReady = true;
            SyncTransform();
            ModLogger.Info($"SetupCanvas 完成 scale={_scale:F4}");
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"SetupCanvas 异常: {ex}");
        }
    }

    private void MoveNodesToAbove(Node parent, string[] names)
    {
        foreach (var name in names)
        {
            var node = parent.GetNodeOrNull<Node>(name);
            if (node == null) continue;
            int origIdx = node.GetIndex();
            _movedNodes.Add((node, parent, origIdx));
            node.Reparent(_clAbove, keepGlobalTransform: true);
            ModLogger.Info($"{name} → layer={LayerAbove}");
        }
    }

    private void TeardownCanvas()
    {
        if (_svRid.IsValid && _mapCanvasRid.IsValid)
        {
            try { RenderingServer.ViewportRemoveCanvas(_svRid, _mapCanvasRid); }
            catch (System.Exception ex) { ModLogger.Warn($"RemoveCanvas: {ex.Message}"); }
        }
        _canvasReady = false;
        RestoreAll();
    }

    private void RestoreAll()
    {
        // 逆序还原所有移动的节点
        for (int i = _movedNodes.Count - 1; i >= 0; i--)
        {
            var (node, parent, idx) = _movedNodes[i];
            if (node == null || !GodotObject.IsInstanceValid(node)) continue;
            if (parent == null || !GodotObject.IsInstanceValid(parent)) continue;
            if (node.GetParent() == parent) continue;
            try
            {
                node.Reparent(parent, keepGlobalTransform: true);
                parent.MoveChild(node, idx);
            }
            catch (System.Exception ex) { ModLogger.Warn($"还原 {node.Name}: {ex.Message}"); }
        }
        _movedNodes.Clear();

        // 还原 TheMap
        if (_mapContainer != null && GodotObject.IsInstanceValid(_mapContainer)
            && _mapOrigParent != null && GodotObject.IsInstanceValid(_mapOrigParent)
            && _mapContainer.GetParent() != _mapOrigParent)
        {
            try
            {
                _mapContainer.Reparent(_mapOrigParent, keepGlobalTransform: true);
                _mapOrigParent.MoveChild(_mapContainer, _mapOrigIndex);
                ModLogger.Info("TheMap 还原完成");
            }
            catch (System.Exception ex) { ModLogger.Warn($"还原 TheMap: {ex.Message}"); }
        }
        _mapOrigParent = null;

        // 销毁 CanvasLayer
        if (_clAbove != null && GodotObject.IsInstanceValid(_clAbove)) { _clAbove.QueueFree(); _clAbove = null; }
        if (_clMap != null && GodotObject.IsInstanceValid(_clMap)) { _clMap.QueueFree(); _clMap = null; }
    }

    // ──────────────────────────────────────────────────────────────
    public void SyncTransform()
    {
        if (!_canvasReady || !_svRid.IsValid || !_mapCanvasRid.IsValid) return;
        if (_mapContainer == null || !GodotObject.IsInstanceValid(_mapContainer)) return;

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

    private void OnFramePostDraw()
    {
        if (!Visible || !_canvasReady) return;
        Callable.From(OnFrameMainThread).CallDeferred();
    }

    private void OnFrameMainThread()
    {
        if (!Visible || !_canvasReady) return;
        if (_mapContainer == null || !GodotObject.IsInstanceValid(_mapContainer)) return;
        SyncTransform();
        UpdateViewportIndicator();
    }

    // ──────────────────────────────────────────────────────────────
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