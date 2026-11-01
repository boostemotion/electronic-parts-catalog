using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace ComponentCatalogWinUI.Services;

public static class UiMotion
{
    public static void ConfigurePanelMotion(UIElement element, bool fromRight = true)
    {
        var uiSettings = new UISettings();
        if (!uiSettings.AnimationsEnabled) return;

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        var translation = compositor.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0.0f, new Vector3(fromRight ? 40f : -40f, 0, 0));
        translation.InsertKeyFrame(1.0f, new Vector3(0, 0, 0));
        translation.Duration = TimeSpan.FromMilliseconds(280);
        translation.Target = "Translation";

        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0.0f, 0.0f);
        opacity.InsertKeyFrame(1.0f, 1.0f);
        opacity.Duration = TimeSpan.FromMilliseconds(220);
        opacity.Target = "Opacity";

        var group = compositor.CreateAnimationGroup();
        group.Add(translation);
        group.Add(opacity);

        ElementCompositionPreview.SetImplicitShowAnimation(element, group);
    }
}
