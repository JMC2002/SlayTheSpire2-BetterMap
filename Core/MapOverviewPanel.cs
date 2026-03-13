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
    private const float BaseBottomPadding = 60f;
    private const float BaseInnerPadding = 6f;
    private const float BgAlpha = 0.88f;
    // ===============================================================

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

    // 为 TheMap 创建的专属 CanvasLayer
    private CanvasLayer _mapCanvasLayer;
    // TheMap 的原始父节点和索引，用于还原
    private Node _mapOriginalParent;
    private int _mapOriginalIndex;

    private static readonly FieldInfo DictField =
        typeof(NMapScreen).GetField("_mapPointDictionary",
            BindingFlags.NonPublic | BindingFlags.Instance);

    public static MapOverviewPanel Create() =>
        new() { Name = "BetterMapOverviewPanel", Visible = false };

    public void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        ModLogger.Info("MapOverviewPanel.EnsureBuilt()");

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
            CanvasCullMask = 0xffffffff,
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
        ModLogger.Info("MapOverviewPanel.EnsureBuilt() 完成");
    }

    public override void _Ready()
    {
        EnsureBuilt();
        ApplyLayout();
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= OnFramePostDraw;
        RestoreMapToOriginalParent();
    }

    public override void _Process(double delta) { }

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
        ModLogger.Info("MapOverviewPanel ShowPanel");
    }

    public void HidePanel()
    {
        Visible = false;
        if (_svRid.IsValid && _mapCanvasRid.IsValid)
            RenderingServer.ViewportRemoveCanvas(_svRid, _mapCanvasRid);
        _canvasReady = false;
        RestoreMapToOriginalParent();
        ModLogger.Info("MapOverviewPanel HidePanel");
    }

    public void SyncTransform()
    {
        if (!_canvasReady || !_svRid.IsValid || !_mapCanvasRid.IsValid) return;
        if (_mapContainer == null || !GodotObject.IsInstanceValid(_mapContainer)) return;

        var svSize = new Vector2(_sv.Size.X, _sv.Size.Y);
        var mapRange = _worldMax - _worldMin;

        float offsetX = (svSize.X - mapRange.X * _scale) * 0.5f;
        float offsetY = (svSize.Y - mapRange.Y * _scale) * 0.5f;

        var mp = _mapContainer.Position;

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

    private void ApplyLayout()
    {
        if (_background == null || _svc == null) return;
        var vp = GetViewport();
        if (vp == null) return;

        Vector2 screenSize = vp.GetVisibleRect().Size;
        float guiScale = screenSize.Y / 1080f;

        float actualLeft = BaseLeft * guiScale;
        float actualTop = BaseTop * guiScale;
        float actualWidth = BaseWidth * guiScale;
        float actualBottomPadding = BaseBottomPadding * guiScale;
        float actualInnerPadding = BaseInnerPadding * guiScale;

        var mapRange = _worldMax - _worldMin;
        if (mapRange.X < 1f || mapRange.Y < 1f) return;

        float maxDisplayHeight = screenSize.Y - actualTop - actualBottomPadding;
        float innerW = actualWidth - actualInnerPadding * 2f;
        float idealInnerH = innerW * (mapRange.Y / mapRange.X);
        float finalInnerH = Mathf.Min(idealInnerH, maxDisplayHeight - actualInnerPadding * 2f);
        float finalPanelH = finalInnerH + actualInnerPadding * 2f;

        Position = new Vector2(actualLeft, actualTop);
        Size = new Vector2(actualWidth, finalPanelH);

        _background.Size = Size;
        _svc.Position = new Vector2(actualInnerPadding, actualInnerPadding);
        _svc.Size = new Vector2(innerW, finalInnerH);
        _sv.Size = (Vector2I)_svc.Size;

        _scale = Mathf.Min(innerW / mapRange.X, finalInnerH / mapRange.Y);

        ModLogger.Info($"ApplyLayout: Screen={screenSize.X}x{screenSize.Y}, GuiScale={guiScale:F2}, PanelWidth={actualWidth}");
    }

    private void DeferredShow()
    {
        ApplyLayout();
        SetupCanvas();
        ModLogger.Info("DeferredShow 完成");
    }

    private void SetupCanvas()
    {
        if (_mapContainer == null || _mapScreen == null) return;
        try
        {
            // ── 步骤1：把 TheMap 移入专属 CanvasLayer，获得独立 Canvas ──
            _mapOriginalParent = _mapContainer.GetParent();
            _mapOriginalIndex = _mapContainer.GetIndex();

            // layer=0 使坐标系与普通 Control 一致，不影响游戏内鼠标/坐标计算
            _mapCanvasLayer = new CanvasLayer
            {
                Name = "BetterMapCanvasLayer",
                Layer = 0,
            };
            _mapScreen.AddChild(_mapCanvasLayer);

            // keep_global_transform=true：保持 TheMap 视觉位置不变
            _mapContainer.Reparent(_mapCanvasLayer, true);

            ModLogger.Info($"SetupCanvas: TheMap reparented，new canvas={_mapContainer.GetCanvas()}");

            // ── 步骤2：把独立 Canvas 挂到 SubViewport ──
            _svRid = _sv.GetViewportRid();
            _mapCanvasRid = _mapContainer.GetCanvas();

            ModLogger.Info($"SetupCanvas: svRid={_svRid} mapCanvasRid={_mapCanvasRid}");

            if (!_svRid.IsValid || !_mapCanvasRid.IsValid)
            {
                ModLogger.Warn("SetupCanvas: RID 无效，跳过");
                return;
            }

            RenderingServer.ViewportAttachCanvas(_svRid, _mapCanvasRid);

            var svSize = new Vector2(_sv.Size.X, _sv.Size.Y);
            var mapRange = _worldMax - _worldMin;
            _scale = Mathf.Min(svSize.X / mapRange.X, svSize.Y / mapRange.Y);

            _canvasReady = true;
            SyncTransform();

            ModLogger.Info($"SetupCanvas: scale={_scale:F4} 完成");
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"SetupCanvas 异常: {ex}");
        }
    }

    private void RestoreMapToOriginalParent()
    {
        if (_mapContainer == null || !GodotObject.IsInstanceValid(_mapContainer)) return;
        if (_mapOriginalParent == null || !GodotObject.IsInstanceValid(_mapOriginalParent)) return;
        if (_mapContainer.GetParent() == _mapOriginalParent) return;

        try
        {
            _mapContainer.Reparent(_mapOriginalParent, true);
            _mapOriginalParent.MoveChild(_mapContainer, _mapOriginalIndex);
            ModLogger.Info("RestoreMapToOriginalParent: 还原完成");
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"RestoreMapToOriginalParent 异常: {ex.Message}");
        }
        finally
        {
            if (_mapCanvasLayer != null && GodotObject.IsInstanceValid(_mapCanvasLayer))
            {
                _mapCanvasLayer.QueueFree();
                _mapCanvasLayer = null;
            }
            _mapOriginalParent = null;
        }
    }

    private void UpdateViewportIndicator()
    {
        if (_viewportIndicator == null || _mapScreen == null || _mapContainer == null) return;

        var svcSize = _svc.Size;
        var mapRange = _worldMax - _worldMin;

        float miniScale = _scale;
        float offX = (svcSize.X - mapRange.X * miniScale) * 0.5f;
        float offY = (svcSize.Y - mapRange.Y * miniScale) * 0.5f;

        float visTop = -_mapContainer.Position.Y;

        float left = offX;
        float top = (visTop - _worldMin.Y) * miniScale + offY;
        float w = mapRange.X * miniScale;
        float h = _mapScreen.Size.Y * miniScale;

        float drawTop = Mathf.Max(top, 0);
        float drawBot = Mathf.Min(top + h, svcSize.Y);
        float drawH = Mathf.Max(drawBot - drawTop, 0);

        _viewportIndicator.Position = new Vector2(left, drawTop);
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

        var margin = new Vector2(80f, 100f);
        _worldMin -= margin;
        _worldMax += margin;
    }
}