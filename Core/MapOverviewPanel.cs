using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace BetterMap.Core;

public partial class MapOverviewPanel : Control
{
    // ================== 基准参数 (以 1080P 为基准) ==================
    // 这里的数值你可以按照 1080P 下的感觉来设置
    private const float BaseLeft = 100f;          // 1080P 下左边距
    private const float BaseTop = 150f;          // 1080P 下顶边距
    private const float BaseWidth = 280f;        // 1080P 下面板宽度
    private const float BaseBottomPadding = 60f; // 1080P 下底部留白
    private const float BaseInnerPadding = 6f;   // 1080P 下内边距
    private const float BgAlpha = 0.88f;        // 背景透明度
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
    private Rid _originalCanvasRid;
    private bool _canvasReady;
    private float _scale;

    // 删除了 _uiHiddenNodes 列表

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
            // 确保视口可以看到所有层
            CanvasCullMask = 0xffffffff
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
        // 删除了 RestoreVisibilityLayers 调用
        if (_svRid.IsValid && _originalCanvasRid.IsValid)
            RenderingServer.ViewportRemoveCanvas(_svRid, _originalCanvasRid);
        _canvasReady = false;
        ModLogger.Info("MapOverviewPanel HidePanel");
    }

    // 同步变换逻辑微调：确保地图在视口内是靠顶或者居中的
    public void SyncTransform()
    {
        if (!_canvasReady || !_svRid.IsValid || !_originalCanvasRid.IsValid) return;
        if (_mapContainer == null || !GodotObject.IsInstanceValid(_mapContainer)) return;

        var svSize = new Vector2(_sv.Size.X, _sv.Size.Y);
        var mapRange = _worldMax - _worldMin;

        // 计算水平和垂直的居中偏移
        float offsetX = (svSize.X - mapRange.X * _scale) * 0.5f;
        float offsetY = (svSize.Y - mapRange.Y * _scale) * 0.5f;

        var mp = _mapContainer.Position;

        // 构建变换矩阵
        var t = new Transform2D(
            new Vector2(_scale, 0),
            new Vector2(0, _scale),
            new Vector2(-(mp.X + _worldMin.X) * _scale + offsetX,
                        -(mp.Y + _worldMin.Y) * _scale + offsetY)
        );
        RenderingServer.ViewportSetCanvasTransform(_svRid, _originalCanvasRid, t);
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

        // 【核心逻辑】计算 GUI 缩放因子 (以 1080P 的高度为参考)
        // 这样在 4K (2160P) 下，guiScale 就会是 2.0
        float guiScale = screenSize.Y / 1080f;

        // 根据缩放因子计算当前分辨率下的实际像素值
        float actualLeft = BaseLeft * guiScale;
        float actualTop = BaseTop * guiScale;
        float actualWidth = BaseWidth * guiScale;
        float actualBottomPadding = BaseBottomPadding * guiScale;
        float actualInnerPadding = BaseInnerPadding * guiScale;

        var mapRange = _worldMax - _worldMin;
        if (mapRange.X < 1f || mapRange.Y < 1f) return;

        // 计算可用高度
        float maxDisplayHeight = screenSize.Y - actualTop - actualBottomPadding;
        float innerW = actualWidth - actualInnerPadding * 2f;

        // 根据地图比例计算理想高度
        float idealInnerH = innerW * (mapRange.Y / mapRange.X);
        float finalInnerH = Mathf.Min(idealInnerH, maxDisplayHeight - actualInnerPadding * 2f);
        float finalPanelH = finalInnerH + actualInnerPadding * 2f;

        // 应用计算后的布局位置
        Position = new Vector2(actualLeft, actualTop);
        Size = new Vector2(actualWidth, finalPanelH);

        _background.Size = Size;
        _svc.Position = new Vector2(actualInnerPadding, actualInnerPadding);
        _svc.Size = new Vector2(innerW, finalInnerH);
        _sv.Size = (Vector2I)_svc.Size;

        // 更新地图渲染缩放
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
        if (_mapContainer == null) return;
        try
        {
            _svRid = _sv.GetViewportRid();
            _originalCanvasRid = _mapContainer.GetCanvas();

            if (!_svRid.IsValid || !_originalCanvasRid.IsValid) return;

            RenderingServer.ViewportAttachCanvas(_svRid, _originalCanvasRid);

            var svSize = new Vector2(_sv.Size.X, _sv.Size.Y);
            var mapRange = _worldMax - _worldMin;
            float scaleX = svSize.X / mapRange.X;
            float scaleY = svSize.Y / mapRange.Y;
            _scale = Mathf.Min(scaleX, scaleY);

            _canvasReady = true;
            SyncTransform();
            // 删除了 SetupVisibilityLayers 调用

            ModLogger.Info($"SetupCanvas: scale={_scale:F4} 完成");
        }
        catch (System.Exception ex)
        {
            ModLogger.Error($"SetupCanvas 异常: {ex.Message}");
        }
    }

    // 指示框也需要适配新的比例
    private void UpdateViewportIndicator()
    {
        if (_viewportIndicator == null || _mapScreen == null || _mapContainer == null) return;

        var svcSize = _svc.Size;
        var mapRange = _worldMax - _worldMin;

        // 使用当前的全局缩放
        float miniScale = _scale;
        float offX = (svcSize.X - mapRange.X * miniScale) * 0.5f;
        float offY = (svcSize.Y - mapRange.Y * miniScale) * 0.5f;

        float visTop = -_mapContainer.Position.Y;
        float visBot = visTop + _mapScreen.Size.Y;

        float left = offX; // 既然黑框和地图一样宽了，left基本就是offX
        float top = (visTop - _worldMin.Y) * miniScale + offY;
        float w = mapRange.X * miniScale;
        float h = (_mapScreen.Size.Y) * miniScale;

        // 裁剪到视口内
        float drawTop = Mathf.Max(top, 0);
        float drawBot = Mathf.Min(top + h, svcSize.Y);
        float drawH = Mathf.Max(drawBot - drawTop, 0);

        _viewportIndicator.Position = new Vector2(left, drawTop);
        _viewportIndicator.Size = new Vector2(w, drawH);

        // 如果指示框超出了视口（说明那部分地图不在当前显示的面板里），隐藏它或裁剪
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