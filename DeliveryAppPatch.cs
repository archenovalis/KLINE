using HarmonyLib;
using ScheduleOne.Delivery;
using ScheduleOne.UI.Phone.Delivery;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using ScheduleOne.DevUtilities;
using UnityEngine.Events;
using MelonLoader;
using UnityEngine.EventSystems;

namespace KLINE
{
  [HarmonyPatch(typeof(DeliveryApp))]
  public class DeliveryAppPatch
  {
    private static bool hasInitialized = false;
    private static readonly HashSet<string> processedDeliveryIDs = new HashSet<string>();

    private static void RefreshLayoutGroupsImmediateAndRecursive(GameObject root)
    {
      foreach (var layout in root.GetComponentsInChildren<LayoutGroup>(true))
      {
        LayoutRebuilder.ForceRebuildLayoutImmediate(layout.GetComponent<RectTransform>());
      }
    }

    private static void LogRectTransform(string prefix, RectTransform rt)
    {
      MelonLogger.Msg($"{prefix}: anchorMin={rt.anchorMin}, anchorMax={rt.anchorMax}, anchoredPosition={rt.anchoredPosition}, sizeDelta={rt.sizeDelta}, offsetMin={rt.offsetMin}, offsetMax={rt.offsetMax}, localPosition={rt.localPosition}, localScale={rt.localScale}, rect={rt.rect}");
    }

    private static void LogComponentDetails(GameObject go, string prefix)
    {
      MelonLogger.Msg($"{prefix} Components:");
      foreach (var component in go.GetComponents<Component>())
      {
        if (component == null) continue;
        MelonLogger.Msg($"  - {component.GetType().Name}");
        if (component is ScrollRect scrollRect)
        {
          MelonLogger.Msg($"    ScrollRect: horizontal={scrollRect.horizontal}, vertical={scrollRect.vertical}, scrollSensitivity={scrollRect.scrollSensitivity}, movementType={scrollRect.movementType}, inertia={scrollRect.inertia}, decelerationRate={scrollRect.decelerationRate}, verticalScrollbarVisibility={scrollRect.verticalScrollbarVisibility}, verticalScrollbarSpacing={scrollRect.verticalScrollbarSpacing}");
          if (scrollRect.content != null) LogRectTransform("    Content", scrollRect.content);
          if (scrollRect.viewport != null) LogRectTransform("    Viewport", scrollRect.viewport);
          if (scrollRect.verticalScrollbar != null) MelonLogger.Msg($"    VerticalScrollbar: active={scrollRect.verticalScrollbar.gameObject.activeSelf}");
        }
        else if (component is CanvasGroup canvasGroup)
        {
          MelonLogger.Msg($"    CanvasGroup: alpha={canvasGroup.alpha}, blocksRaycasts={canvasGroup.blocksRaycasts}, interactable={canvasGroup.interactable}");
        }
        else if (component is VerticalLayoutGroup vlg)
        {
          MelonLogger.Msg($"    VerticalLayoutGroup: childAlignment={vlg.childAlignment}, childForceExpandWidth={vlg.childForceExpandWidth}, childForceExpandHeight={vlg.childForceExpandHeight}, childControlWidth={vlg.childControlWidth}, childControlHeight={vlg.childControlHeight}, spacing={vlg.spacing}, padding={vlg.padding}");
        }
        else if (component is ContentSizeFitter csf)
        {
          MelonLogger.Msg($"    ContentSizeFitter: horizontalFit={csf.horizontalFit}, verticalFit={csf.verticalFit}");
        }
        else if (component is Mask mask)
        {
          MelonLogger.Msg($"    Mask: showMaskGraphic={mask.showMaskGraphic}");
        }
      }
    }

    [HarmonyPostfix]
    [HarmonyPatch("Awake")]
    public static void AwakePostfix(DeliveryApp __instance)
    {
      if (hasInitialized || __instance.StatusDisplayContainer.GetComponentInParent<ScrollRect>() != null)
      {
        MelonLogger.Msg("Skipping ScrollRect setup; already initialized or ScrollRect exists.");
        return;
      }
      hasInitialized = true;

      var container = __instance.StatusDisplayContainer;
      var mainScrollRect = __instance.MainScrollRect;

      // Ensure container is active and visible
      container.gameObject.SetActive(true);
      var containerCanvasGroup = container.GetComponent<CanvasGroup>() ?? container.gameObject.AddComponent<CanvasGroup>();
      containerCanvasGroup.alpha = 1f;
      containerCanvasGroup.blocksRaycasts = true;
      containerCanvasGroup.interactable = true;

      // Log original MainScrollRect and StatusDisplayContainer details
      if (mainScrollRect != null)
      {
        LogRectTransform("MainScrollRect RectTransform", mainScrollRect.GetComponent<RectTransform>());
        LogComponentDetails(mainScrollRect.gameObject, "MainScrollRect");
      }
      LogRectTransform("StatusDisplayContainer RectTransform (Before)", container);
      LogComponentDetails(container.gameObject, "StatusDisplayContainer (Before)");

      // Adjust existing VerticalLayoutGroup if present
      var layout = container.GetComponent<VerticalLayoutGroup>();
      if (layout != null)
      {
        layout.childAlignment = TextAnchor.UpperLeft;
        MelonLogger.Msg("Adjusted VerticalLayoutGroup: childAlignment set to UpperLeft");
      }

      // Create ScrollRect parent
      var scrollObj = new GameObject("StatusScrollRect");
      scrollObj.transform.SetParent(container.transform.parent, false);
      scrollObj.transform.SetAsLastSibling(); // Ensure above other UI
      var scrollRect = scrollObj.AddComponent<ScrollRect>();
      var scrollRectTransform = scrollObj.GetComponent<RectTransform>();
      scrollRectTransform.anchorMin = new Vector2(0.65f, 0f); // Start where MainScrollRect ends
      scrollRectTransform.anchorMax = new Vector2(1f, 1f);
      scrollRectTransform.offsetMin = new Vector2(0, 0);
      scrollRectTransform.offsetMax = new Vector2(-10, -50);
      scrollRectTransform.localScale = container.localScale;
      scrollObj.layer = LayerMask.NameToLayer("UI");
      scrollObj.AddComponent<CanvasRenderer>();

      // Create Viewport
      var viewportObj = new GameObject("Viewport");
      viewportObj.transform.SetParent(scrollObj.transform, false);
      var viewportRect = viewportObj.AddComponent<RectTransform>();
      viewportRect.anchorMin = new Vector2(0, 0);
      viewportRect.anchorMax = new Vector2(1, 1);
      viewportRect.offsetMin = new Vector2(0, 0);
      viewportRect.offsetMax = new Vector2(-15, 0); // Space for scrollbar
      viewportRect.sizeDelta = new Vector2(-15, 400); // Fixed height for viewport
      viewportRect.localScale = Vector3.one;
      viewportObj.layer = LayerMask.NameToLayer("UI");

      // Add Mask
      var mask = viewportObj.AddComponent<Mask>();
      mask.showMaskGraphic = false;
      var viewportImage = viewportObj.AddComponent<Image>();
      viewportImage.color = new Color(1, 1, 1, 0);
      viewportImage.raycastTarget = true;

      // Move StatusDisplayContainer to Viewport
      container.SetParent(viewportRect, false);
      scrollRect.content = container;
      scrollRect.viewport = viewportRect;

      // Configure ScrollRect to match MainScrollRect
      scrollRect.horizontal = false;
      scrollRect.vertical = true;
      scrollRect.scrollSensitivity = 10f; // Match MainScrollRect
      scrollRect.movementType = ScrollRect.MovementType.Elastic; // Match MainScrollRect
      scrollRect.inertia = true;
      scrollRect.decelerationRate = 0.135f;
      scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
      scrollRect.verticalScrollbarSpacing = -3f; // Match MainScrollRect

      // Create Scrollbar
      var scrollbarObj = new GameObject("StatusScrollbar");
      scrollbarObj.transform.SetParent(scrollObj.transform, false);
      var scrollbarRect = scrollbarObj.AddComponent<RectTransform>();
      scrollbarRect.anchorMin = new Vector2(1, 0);
      scrollbarRect.anchorMax = new Vector2(1, 1);
      scrollbarRect.offsetMin = new Vector2(-15, 0);
      scrollbarRect.offsetMax = new Vector2(0, 0);
      scrollbarRect.sizeDelta = new Vector2(15, 0);
      scrollbarRect.localScale = Vector3.one;
      var scrollbar = scrollbarObj.AddComponent<Scrollbar>();
      scrollbar.direction = Scrollbar.Direction.BottomToTop;
      scrollbarObj.layer = LayerMask.NameToLayer("UI");
      scrollbarObj.AddComponent<CanvasRenderer>();

      // Create Scrollbar Handle
      var handleObj = new GameObject("Handle");
      handleObj.transform.SetParent(scrollbarObj.transform, false);
      var handleRect = handleObj.AddComponent<RectTransform>();
      handleRect.anchorMin = new Vector2(0, 0);
      handleRect.anchorMax = new Vector2(1, 1);
      handleRect.sizeDelta = new Vector2(0, 0);
      handleRect.localScale = Vector3.one;
      var handleImage = handleObj.AddComponent<Image>();
      handleImage.color = new Color(1, 1, 1, 1);
      handleImage.raycastTarget = true;
      scrollbar.targetGraphic = handleImage;
      scrollbar.handleRect = handleRect;

      scrollRect.verticalScrollbar = scrollbar;

      // Log setup
      LogRectTransform("StatusScrollRect RectTransform", scrollRectTransform);
      MelonLogger.Msg($"ScrollRect setup: content.sizeDelta={container.sizeDelta}, viewport.sizeDelta={viewportRect.sizeDelta}, scrollSensitivity={scrollRect.scrollSensitivity}");
      var parentCanvas = __instance.GetComponentInParent<Canvas>();
      if (parentCanvas != null)
      {
        MelonLogger.Msg($"Parent Canvas: enabled={parentCanvas.enabled}, scaleFactor={parentCanvas.scaleFactor}, sortingOrder={parentCanvas.sortingOrder}, renderMode={parentCanvas.renderMode}");
      }
      MelonLogger.Msg($"EventSystem: {(EventSystem.current != null ? "Active" : "Missing")}");

      // Force layout rebuild
      RefreshLayoutGroupsImmediateAndRecursive(container.gameObject);
    }

    [HarmonyPrefix]
    [HarmonyPatch("CreateDeliveryStatusDisplay")]
    public static bool CreateDeliveryStatusDisplayPrefix(DeliveryInstance instance, DeliveryApp __instance)
    {
      if (instance == null || processedDeliveryIDs.Contains(instance.DeliveryID))
      {
        MelonLogger.Msg($"Skipped CreateDeliveryStatusDisplay for {instance?.DeliveryID} (duplicate or null).");
        return false;
      }

      var deliveryStatusDisplay = Object.Instantiate(__instance.StatusDisplayPrefab, __instance.StatusDisplayContainer);
      deliveryStatusDisplay.AssignDelivery(instance);
      deliveryStatusDisplay.gameObject.SetActive(true);

      // Ensure visibility and input
      var displayCanvasGroup = deliveryStatusDisplay.GetComponent<CanvasGroup>() ?? deliveryStatusDisplay.gameObject.AddComponent<CanvasGroup>();
      displayCanvasGroup.alpha = 1f;
      displayCanvasGroup.blocksRaycasts = true;
      displayCanvasGroup.interactable = true;
      deliveryStatusDisplay.gameObject.layer = LayerMask.NameToLayer("UI");
      deliveryStatusDisplay.transform.localPosition = new Vector3(deliveryStatusDisplay.transform.localPosition.x, deliveryStatusDisplay.transform.localPosition.y, 0);

      // Force opaque colors
      foreach (var image in deliveryStatusDisplay.GetComponentsInChildren<Image>(true))
      {
        if (image.color.a < 1f)
        {
          image.color = new Color(image.color.r, image.color.g, image.color.b, 1f);
          MelonLogger.Msg($"Forced Image alpha to 1 on {image.gameObject.name}");
        }
        image.raycastTarget = true;
      }
      foreach (var text in deliveryStatusDisplay.GetComponentsInChildren<Text>(true))
      {
        if (text.color.a < 1f)
        {
          text.color = new Color(text.color.r, text.color.g, text.color.b, 1f);
          MelonLogger.Msg($"Forced Text alpha to 1 on {text.gameObject.name}");
        }
        text.raycastTarget = true;
      }

      __instance.statusDisplays.Add(deliveryStatusDisplay);
      processedDeliveryIDs.Add(instance.DeliveryID);

      __instance.SortStatusDisplays();
      RefreshLayoutGroupsImmediateAndRecursive(__instance.StatusDisplayContainer.gameObject);
      __instance.RefreshNoDeliveriesIndicator();

      var contentHeight = LayoutUtility.GetPreferredHeight(__instance.StatusDisplayContainer);
      MelonLogger.Msg($"Created DeliveryStatusDisplay for {instance.DeliveryID}, active={deliveryStatusDisplay.gameObject.activeSelf}, alpha={displayCanvasGroup.alpha}, childImages={deliveryStatusDisplay.GetComponentsInChildren<Image>(true).Length}, childTexts={deliveryStatusDisplay.GetComponentsInChildren<Text>(true).Length}, contentHeight={contentHeight}");

      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("RefreshContent")]
    public static bool RefreshContentPrefix(DeliveryApp __instance, bool keepScrollPosition)
    {
      float mainScrollPos = __instance.MainScrollRect.verticalNormalizedPosition;
      var statusScrollRect = __instance.StatusDisplayContainer.GetComponentInParent<ScrollRect>();
      float statusScrollPos = statusScrollRect?.verticalNormalizedPosition ?? 0f;

      if (__instance.MainLayoutGroup != null)
      {
        RefreshLayoutGroupsImmediateAndRecursive(__instance.MainLayoutGroup.gameObject);
      }

      if (__instance.StatusDisplayContainer != null)
      {
        RefreshLayoutGroupsImmediateAndRecursive(__instance.StatusDisplayContainer.gameObject);
      }

      // Ensure StatusScrollRect is last sibling
      if (statusScrollRect != null)
      {
        statusScrollRect.transform.SetAsLastSibling();
      }

      if (keepScrollPosition)
      {
        __instance.MainScrollRect.verticalNormalizedPosition = mainScrollPos;
        if (statusScrollRect != null)
        {
          statusScrollRect.verticalNormalizedPosition = statusScrollPos;
        }
      }

      // Log content visibility and scrollbar state
      var contentCanvasGroup = __instance.StatusDisplayContainer.GetComponent<CanvasGroup>();
      var contentHeight = LayoutUtility.GetPreferredHeight(__instance.StatusDisplayContainer);
      var viewportHeight = statusScrollRect?.viewport?.rect.height ?? __instance.StatusDisplayContainer.rect.height;
      var parentCanvas = __instance.GetComponentInParent<Canvas>();
      MelonLogger.Msg($"RefreshContent: content.active={__instance.StatusDisplayContainer.gameObject.activeSelf}, content.alpha={(contentCanvasGroup ? contentCanvasGroup.alpha : 1f)}, childCount={__instance.StatusDisplayContainer.childCount}, siblingIndex={__instance.StatusDisplayContainer.parent.GetSiblingIndex()}, contentHeight={contentHeight}, viewportHeight={viewportHeight}, scrollbarActive={(statusScrollRect?.verticalScrollbar != null ? statusScrollRect.verticalScrollbar.gameObject.activeSelf : false)}");
      if (parentCanvas != null)
      {
        MelonLogger.Msg($"Parent Canvas (RefreshContent): enabled={parentCanvas.enabled}, scaleFactor={parentCanvas.scaleFactor}, sortingOrder={parentCanvas.sortingOrder}, renderMode={parentCanvas.renderMode}");
      }
      foreach (var sibling in __instance.StatusDisplayContainer.parent.parent.GetComponentsInChildren<RectTransform>(true))
      {
        if (sibling != __instance.StatusDisplayContainer.parent && sibling.gameObject.activeSelf && sibling.GetComponent<CanvasGroup>()?.alpha > 0)
        {
          MelonLogger.Msg($"Potential overlapping sibling: {sibling.gameObject.name}, siblingIndex={sibling.GetSiblingIndex()}, alpha={(sibling.GetComponent<CanvasGroup>()?.alpha ?? 1f)}");
          LogRectTransform($"  Bounds of {sibling.gameObject.name}", sibling);
        }
      }

      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("RefreshNoDeliveriesIndicator")]
    public static bool RefreshNoDeliveriesIndicatorPrefix(DeliveryApp __instance)
    {
      if (__instance.NoDeliveriesIndicator != null)
      {
        __instance.NoDeliveriesIndicator.gameObject.SetActive(__instance.statusDisplays.Count == 0);
        var indicatorCanvasGroup = __instance.NoDeliveriesIndicator.GetComponent<CanvasGroup>() ?? __instance.NoDeliveriesIndicator.gameObject.AddComponent<CanvasGroup>();
        indicatorCanvasGroup.alpha = __instance.statusDisplays.Count == 0 ? 1f : 0f;
        indicatorCanvasGroup.blocksRaycasts = true;
        indicatorCanvasGroup.interactable = true;
        __instance.NoDeliveriesIndicator.transform.localPosition = new Vector3(__instance.NoDeliveriesIndicator.transform.localPosition.x, __instance.NoDeliveriesIndicator.transform.localPosition.y, 0);
        MelonLogger.Msg($"NoDeliveriesIndicator: active={__instance.NoDeliveriesIndicator.gameObject.activeSelf}, alpha={indicatorCanvasGroup.alpha}");
      }
      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("DeliveryCompleted")]
    public static bool DeliveryCompletedPrefix(DeliveryInstance instance, DeliveryApp __instance)
    {
      if (instance == null)
      {
        return false;
      }

      var display = __instance.statusDisplays.FirstOrDefault(d => d != null && d.DeliveryInstance != null && d.DeliveryInstance.DeliveryID == instance.DeliveryID);
      if (display != null)
      {
        __instance.statusDisplays.Remove(display);
        Object.Destroy(display.gameObject);
        processedDeliveryIDs.Remove(instance.DeliveryID);
        RefreshLayoutGroupsImmediateAndRecursive(__instance.StatusDisplayContainer.gameObject);
        MelonLogger.Msg($"Removed DeliveryStatusDisplay for {instance.DeliveryID}.");
      }

      __instance.RefreshNoDeliveriesIndicator();
      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("SortStatusDisplays")]
    public static bool SortStatusDisplaysPrefix(DeliveryApp __instance)
    {
      __instance.statusDisplays = __instance.statusDisplays
          .Where(d => d != null && d.DeliveryInstance != null)
          .OrderBy(d => d.DeliveryInstance.GetTimeStatus())
          .ToList();

      for (int i = 0; i < __instance.statusDisplays.Count; i++)
      {
        __instance.statusDisplays[i].transform.SetSiblingIndex(i);
      }

      RefreshLayoutGroupsImmediateAndRecursive(__instance.StatusDisplayContainer.gameObject);
      return false;
    }
  }
}