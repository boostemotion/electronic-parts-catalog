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

        try
        {
            // Translation is an opt-in composition property on XAML elements. Animating "Offset"
            // instead would override the layout offset and drag the element to the window origin.
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

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
        catch (Exception exception)
        {
            AppLogger.WritePage($"UiMotion.ConfigurePanelMotion error: {exception.Message}");
        }
    }

    /// <summary>
    /// Soft cross-fade used when a filter replaces the visible result set. The list is swapped
    /// in one batch by the caller, then this short animation reintroduces it instead of letting
    /// cards pop in/out abruptly.
    /// </summary>
    public static void PlayListRefresh(UIElement element)
    {
        PlayFade(element, 0.35f, 8f, 150, 170);
    }

    /// <summary>
    /// Cross-fade used when the navigation frame swaps pages. This replaces the framework
    /// NavigationThemeTransition, whose animation could not be interrupted safely: rapid clicks
    /// crashed Microsoft.UI.Xaml.dll and left a ghost of the previous page on screen.
    /// </summary>
    public static void PlayPageSwap(UIElement element)
    {
        PlayFade(element, 0.0f, 10f, 120, 150);
    }

    private static void PlayFade(UIElement element, float fromOpacity, float lift, int fadeMilliseconds, int slideMilliseconds)
    {
        try
        {
            var uiSettings = new UISettings();
            if (!uiSettings.AnimationsEnabled) return;

            // Use the opt-in Translation property, not Offset: Offset carries the element's
            // layout position, so animating it makes the element render at the window origin.
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0.0f, fromOpacity);
            opacity.InsertKeyFrame(1.0f, 1.0f);
            opacity.Duration = TimeSpan.FromMilliseconds(fadeMilliseconds);
            visual.StartAnimation("Opacity", opacity);

            var translation = compositor.CreateVector3KeyFrameAnimation();
            translation.InsertKeyFrame(0.0f, new Vector3(0, lift, 0));
            translation.InsertKeyFrame(1.0f, Vector3.Zero);
            translation.Duration = TimeSpan.FromMilliseconds(slideMilliseconds);
            visual.StartAnimation("Translation", translation);
        }
        catch (Exception exception)
        {
            AppLogger.WritePage($"UiMotion.PlayFade error: {exception.Message}");
        }
    }
}
