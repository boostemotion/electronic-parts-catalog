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

    /// <summary>
    /// Soft cross-fade used when a filter replaces the visible result set. The list is swapped
    /// in one batch by the caller, then this short animation reintroduces it instead of letting
    /// cards pop in/out abruptly.
    /// </summary>
    public static void PlayListRefresh(UIElement element)
    {
        try
        {
            var uiSettings = new UISettings();
            if (!uiSettings.AnimationsEnabled) return;

            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0.0f, 0.35f);
            opacity.InsertKeyFrame(1.0f, 1.0f);
            opacity.Duration = TimeSpan.FromMilliseconds(150);
            visual.StartAnimation("Opacity", opacity);

            var translation = compositor.CreateVector3KeyFrameAnimation();
            translation.InsertKeyFrame(0.0f, new Vector3(0, 8f, 0));
            translation.InsertKeyFrame(1.0f, Vector3.Zero);
            translation.Duration = TimeSpan.FromMilliseconds(170);
            visual.StartAnimation("Translation", translation);
        }
        catch
        {
            // Animation is cosmetic; never let it break filtering.
        }
    }

    /// <summary>
    /// Cross-fade used when the navigation frame swaps pages. This replaces the framework
    /// NavigationThemeTransition, whose animation could not be interrupted safely: rapid clicks
    /// crashed Microsoft.UI.Xaml.dll and left a ghost of the previous page on screen.
    /// </summary>
    public static void PlayPageSwap(UIElement element)
    {
        try
        {
            var uiSettings = new UISettings();
            if (!uiSettings.AnimationsEnabled) return;

            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0.0f, 0.0f);
            opacity.InsertKeyFrame(1.0f, 1.0f);
            opacity.Duration = TimeSpan.FromMilliseconds(120);
            visual.StartAnimation("Opacity", opacity);

            var translation = compositor.CreateVector3KeyFrameAnimation();
            translation.InsertKeyFrame(0.0f, new Vector3(0, 10f, 0));
            translation.InsertKeyFrame(1.0f, Vector3.Zero);
            translation.Duration = TimeSpan.FromMilliseconds(150);
            visual.StartAnimation("Translation", translation);
        }
        catch
        {
            // Animation is cosmetic; never let it break navigation.
        }
    }
}
