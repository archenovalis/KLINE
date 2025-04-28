using MelonLoader;
using HarmonyLib;
using ScheduleOne;
using ScheduleOne.Delivery;
using ScheduleOne.ItemFramework;
using ScheduleOne.Money;
using ScheduleOne.UI.Phone.Delivery;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using ScheduleOne.DevUtilities;
using Unity.Mathematics;
using UnityEngine.Events;
using FishNet.Managing.Timing;

[assembly: MelonInfo(typeof(KLINE.KLINEMod), "KLINE", "1.0.10", "Archie")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace KLINE
{
  public class KLINEMod : MelonMod
  {
    public static readonly bool debugLog = true;

    public override void OnInitializeMelon()
    {
      try
      {
        HarmonyInstance.PatchAll();
        if (debugLog) MelonLogger.Msg("KLINE loaded!");
      }
      catch (Exception e)
      {
        MelonLogger.Error($"Failed to initialize KLINE: {e}");
      }
    }
  }

  [HarmonyPatch(typeof(DeliveryShop))]
  public class DeliveryShopPatch
  {
    private static List<List<StringIntPair>> SplitItemsIntoVehicles(List<StringIntPair> items)
    {
      try
      {
        if (items == null)
        {
          if (KLINEMod.debugLog) MelonLogger.Warning("Items list is null in SplitItemsIntoVehicles.");
          return new List<List<StringIntPair>>();
        }

        List<List<StringIntPair>> vehicleLoads = new List<List<StringIntPair>>();
        List<StringIntPair> currentLoad = new List<StringIntPair>();
        int currentStackCount = 0;
        const int DELIVERY_VEHICLE_SLOT_CAPACITY = 16;

        foreach (StringIntPair item in items)
        {
          ItemDefinition itemDef = Registry.GetItem(item.String);
          if (itemDef == null)
          {
            if (KLINEMod.debugLog) MelonLogger.Warning($"Item definition not found for ID: {item.String}.");
            continue;
          }

          int quantity = item.Int;
          int stackLimit = itemDef.StackLimit;

          while (quantity > 0)
          {
            int stackSize = Mathf.Min(quantity, stackLimit);
            int stacks = Mathf.CeilToInt((float)stackSize / stackLimit);

            if (currentStackCount + stacks > DELIVERY_VEHICLE_SLOT_CAPACITY)
            {
              vehicleLoads.Add(currentLoad);
              currentLoad = new List<StringIntPair>();
              currentStackCount = 0;
            }

            currentLoad.Add(new StringIntPair(item.String, stackSize));
            currentStackCount += stacks;
            quantity -= stackSize;
          }
        }

        if (currentLoad.Count > 0)
        {
          vehicleLoads.Add(currentLoad);
        }

        if (KLINEMod.debugLog) MelonLogger.Msg($"Split {items.Sum(i => i.Int)} items into {vehicleLoads.Count} vehicles.");
        return vehicleLoads;
      }
      catch (Exception e)
      {
        if (KLINEMod.debugLog) MelonLogger.Error($"Error in SplitItemsIntoVehicles: {e}");
        return new List<List<StringIntPair>>();
      }
    }

    private static int CalculateVehicleCount(List<ListingEntry> entries)
    {
      List<StringIntPair> items = entries
          .Where(le => le.SelectedQuantity > 0)
          .Select(le => new StringIntPair(le.MatchingListing.Item.ID, le.SelectedQuantity))
          .ToList();
      return math.max(SplitItemsIntoVehicles(items).Count, 1);
    }

    [HarmonyPrefix]
    [HarmonyPatch("WillCartFitInVehicle")]
    public static bool WillCartFitInVehiclePrefix(ref bool __result)
    {
      __result = true;
      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("GetOrderTotal")]
    public static bool GetOrderTotalPrefix(DeliveryShop __instance, ref float __result)
    {
      float cartCost = __instance.GetCartCost();
      int vehicleCount = CalculateVehicleCount(__instance.listingEntries);
      __result = cartCost + __instance.DeliveryFee * vehicleCount;
      if (KLINEMod.debugLog) MelonLogger.Msg($"Order total: {cartCost} + {__instance.DeliveryFee} * {vehicleCount} = {__result}");
      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("OrderPressed")]
    public static bool OrderPressedPrefix(DeliveryShop __instance)
    {
      string reason;
      if (!__instance.CanOrder(out reason))
      {
        if (KLINEMod.debugLog) MelonLogger.Warning($"Cannot order: {reason}");
        return false;
      }

      float orderTotal = __instance.GetOrderTotal();
      List<StringIntPair> orderItems = __instance.listingEntries
          .Where(le => le.SelectedQuantity > 0)
          .Select(le => new StringIntPair(le.MatchingListing.Item.ID, le.SelectedQuantity))
          .ToList();
      int orderItemCount = orderItems.Sum(i => i.Int);
      int deliveryTime = Mathf.RoundToInt(Mathf.Lerp(60f, 360f, Mathf.Clamp01((float)orderItemCount / 160f)));
      List<List<StringIntPair>> vehicleLoads = SplitItemsIntoVehicles(orderItems);

      foreach (var load in vehicleLoads)
      {
        DeliveryInstance delivery = new DeliveryInstance(
            GUIDManager.GenerateUniqueGUID().ToString(),
            __instance.MatchingShopInterfaceName,
            __instance.destinationProperty.PropertyCode,
            __instance.loadingDockIndex - 1,
            load.ToArray(),
            EDeliveryStatus.InTransit,
            deliveryTime
        );
        NetworkSingleton<DeliveryManager>.Instance.SendDelivery(delivery);
        if (KLINEMod.debugLog) MelonLogger.Msg($"Created delivery {delivery.DeliveryID} with {load.Sum(i => i.Int)} items.");
      }

      NetworkSingleton<MoneyManager>.Instance.CreateOnlineTransaction(
          $"Delivery from {__instance.MatchingShop.ShopName}",
          -orderTotal,
          1f,
          string.Empty
      );
      PlayerSingleton<DeliveryApp>.Instance.PlayOrderSubmittedAnim();
      __instance.ResetCart();
      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("RefreshCart")]
    public static bool RefreshCartPrefix(DeliveryShop __instance)
    {
      __instance.ItemTotalLabel.text = MoneyManager.FormatAmount(__instance.GetCartCost(), false, false);
      __instance.OrderTotalLabel.text = MoneyManager.FormatAmount(__instance.GetOrderTotal(), false, false);
      int vehicleCount = CalculateVehicleCount(__instance.listingEntries);
      float deliveryFeeTotal = __instance.DeliveryFee * vehicleCount;
      __instance.DeliveryFeeLabel.text = MoneyManager.FormatAmount(deliveryFeeTotal, false, false);
      if (KLINEMod.debugLog) MelonLogger.Msg($"Updated DeliveryFeeLabel to {deliveryFeeTotal} for {vehicleCount} vehicles.");
      return false;
    }
  }

  [HarmonyPatch(typeof(DeliveryStatusDisplay))]
  public class DeliveryStatusDisplayPatch
  {
    [HarmonyPrefix]
    [HarmonyPatch("AssignDelivery")]
    public static bool AssignDeliveryPrefix(DeliveryInstance instance, DeliveryStatusDisplay __instance)
    {
      if (instance == null || __instance == null) return false;
      __instance.DeliveryInstance = instance;
      __instance.DestinationLabel.text = $"{instance.Destination.PropertyName} [{instance.LoadingDockIndex + 1}]";
      __instance.ShopLabel.text = instance.StoreName;

      foreach (Transform child in __instance.ItemEntryContainer)
        Object.Destroy(child.gameObject);

      var consolidatedItems = instance.Items
          .GroupBy(item => item.String)
          .Select(group => new StringIntPair(group.Key, group.Sum(item => item.Int)))
          .ToArray();

      foreach (StringIntPair item in consolidatedItems)
      {
        Transform entry = Object.Instantiate(__instance.ItemEntryPrefab, __instance.ItemEntryContainer).GetComponent<RectTransform>();
        ItemDefinition itemDef = Registry.GetItem(item.String);
        entry.Find("Label").GetComponent<Text>().text = $"{item.Int}x {itemDef.Name}";
      }

      int num = Mathf.CeilToInt((float)consolidatedItems.Length / 2f);
      __instance.Rect.sizeDelta = new Vector2(__instance.Rect.sizeDelta.x, 70 + 20 * num);
      __instance.RefreshStatus();
      if (KLINEMod.debugLog) MelonLogger.Msg($"Assigned delivery {instance.DeliveryID} with {consolidatedItems.Length} consolidated items, sizeDelta={__instance.Rect.sizeDelta}");
      return false;
    }
  }

  [HarmonyPatch(typeof(DeliveryApp))]
  public class DeliveryAppPatch
  {
    private static bool hasInitialized = false; // Prevent multiple ScrollRect setups
    private static int initializationCount = 0; // Track initialization attempts

    // Helper to rebuild layout recursively
    private static void RefreshLayoutGroupsImmediateAndRecursive(GameObject root)
    {
      LayoutGroup[] componentsInChildren = root.GetComponentsInChildren<LayoutGroup>(true);
      for (int i = 0; i < componentsInChildren.Length; i++)
      {
        LayoutRebuilder.ForceRebuildLayoutImmediate(componentsInChildren[i].GetComponent<RectTransform>());
      }
      var rootLayout = root.GetComponent<LayoutGroup>();
      if (rootLayout != null)
        LayoutRebuilder.ForceRebuildLayoutImmediate(rootLayout.GetComponent<RectTransform>());
    }

    [HarmonyPrefix]
    [HarmonyPatch("Start")]
    public static bool StartPrefix(DeliveryApp __instance)
    {
      if (!__instance.started)
      {
        __instance.started = true;
        var deliveryManager = NetworkSingleton<DeliveryManager>.Instance;
        // Remove existing listeners to prevent duplicates
        deliveryManager.onDeliveryCreated.RemoveListener(__instance.CreateDeliveryStatusDisplay);
        deliveryManager.onDeliveryCreated.AddListener(new UnityAction<DeliveryInstance>(__instance.CreateDeliveryStatusDisplay));
        deliveryManager.onDeliveryCompleted.RemoveListener(__instance.DeliveryCompleted);
        deliveryManager.onDeliveryCompleted.AddListener(new UnityAction<DeliveryInstance>(__instance.DeliveryCompleted));
        for (int i = 0; i < deliveryManager.Deliveries.Count; i++)
        {
          __instance.CreateDeliveryStatusDisplay(deliveryManager.Deliveries[i]);
        }
        if (KLINEMod.debugLog) MelonLogger.Msg($"Initialized DeliveryApp listeners, deliveries={deliveryManager.Deliveries.Count}");
      }
      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("DeliveryCompleted")]
    public static bool DeliveryCompletedPrefix(DeliveryInstance instance, DeliveryApp __instance)
    {
      if (instance == null || __instance == null) return false;
      DeliveryStatusDisplay display = __instance.statusDisplays.FirstOrDefault(d =>
          d != null && d.DeliveryInstance != null && d.DeliveryInstance.DeliveryID == instance.DeliveryID);
      if (display != null)
      {
        __instance.statusDisplays.Remove(display);
        Object.Destroy(display.gameObject);
        if (KLINEMod.debugLog) MelonLogger.Msg($"Removed display for completed delivery {instance.DeliveryID}.");
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
      return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch("Awake")]
    public static void AwakePostfix(DeliveryApp __instance)
    {
      if (hasInitialized || __instance.StatusDisplayContainer.GetComponent<ScrollRect>() != null)
      {
        if (KLINEMod.debugLog) MelonLogger.Msg($"Skipping ScrollRect setup for DeliveryApp instance {__instance.GetInstanceID()}; already initialized.");
        return;
      }

      initializationCount++;
      hasInitialized = true;

      var container = __instance.StatusDisplayContainer;
      var mainScrollRect = __instance.MainScrollRect;

      // Debug MainScrollRect
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"AwakePostfix #{initializationCount} for DeliveryApp instance {__instance.GetInstanceID()}");
        if (mainScrollRect != null)
        {
          MelonLogger.Msg($"MainScrollRect: viewport={mainScrollRect.viewport?.sizeDelta}, content={mainScrollRect.content?.sizeDelta}, " +
                          $"verticalScrollbar={(mainScrollRect.verticalScrollbar != null ? mainScrollRect.verticalScrollbar.GetComponent<RectTransform>().sizeDelta : "null")}");
        }
        else
        {
          MelonLogger.Warning("MainScrollRect is null.");
        }
      }

      // Validate container size
      Vector2 containerSize = container.sizeDelta;
      if (containerSize.x <= 0 || containerSize.y <= 0)
      {
        containerSize = mainScrollRect != null && mainScrollRect.viewport != null && mainScrollRect.viewport.sizeDelta.x > 0
            ? mainScrollRect.viewport.sizeDelta
            : new Vector2(300, 400);
        container.sizeDelta = containerSize;
        if (KLINEMod.debugLog) MelonLogger.Warning($"Invalid StatusDisplayContainer size; set to {containerSize}.");
      }

      // Debug container
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"StatusDisplayContainer: sizeDelta={container.sizeDelta}, anchorMin={container.anchorMin}, " +
                        $"anchorMax={container.anchorMax}, anchoredPosition={container.anchoredPosition}");
      }

      // Add ScrollRect
      var scrollRect = container.gameObject.AddComponent<ScrollRect>();
      if (mainScrollRect != null)
      {
        scrollRect.horizontal = mainScrollRect.horizontal;
        scrollRect.vertical = mainScrollRect.vertical;
        scrollRect.scrollSensitivity = mainScrollRect.scrollSensitivity;
        scrollRect.movementType = mainScrollRect.movementType;
        scrollRect.elasticity = mainScrollRect.elasticity;
        scrollRect.inertia = mainScrollRect.inertia;
        scrollRect.decelerationRate = mainScrollRect.decelerationRate;
        scrollRect.verticalScrollbarVisibility = mainScrollRect.verticalScrollbarVisibility;
        scrollRect.verticalScrollbarSpacing = mainScrollRect.verticalScrollbarSpacing;
        if (KLINEMod.debugLog) MelonLogger.Msg("Duplicated MainScrollRect parameters.");
      }
      else
      {
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 1f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.elasticity = 0.1f;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.135f;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarSpacing = -3f;
        if (KLINEMod.debugLog) MelonLogger.Warning("MainScrollRect is null; using default ScrollRect parameters.");
      }

      // Set up viewport
      GameObject viewportObj = new GameObject("Viewport");
      viewportObj.transform.SetParent(container.transform, false);
      var viewportRect = viewportObj.AddComponent<RectTransform>();
      viewportRect.anchorMin = container.anchorMin;
      viewportRect.anchorMax = container.anchorMax;
      viewportRect.anchoredPosition = container.anchoredPosition;
      viewportRect.sizeDelta = container.sizeDelta;
      viewportRect.offsetMin = new Vector2(0, 0);
      viewportRect.offsetMax = new Vector2(0, 0);
      viewportObj.AddComponent<Mask>();
      var viewportImage = viewportObj.AddComponent<Image>();
      viewportImage.color = new Color(0, 0, 0, 0);
      scrollRect.viewport = viewportRect;

      // Debug viewport
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"Viewport: sizeDelta={viewportRect.sizeDelta}, offsetMin={viewportRect.offsetMin}, offsetMax={viewportRect.offsetMax}");
      }

      // Create content
      GameObject contentObj = new GameObject("StatusContent");
      contentObj.transform.SetParent(viewportRect, false);
      var contentRect = contentObj.AddComponent<RectTransform>();
      contentRect.anchorMin = new Vector2(0, 1);
      contentRect.anchorMax = new Vector2(1, 1);
      contentRect.pivot = new Vector2(0.5f, 1);
      contentRect.anchoredPosition = Vector2.zero;
      contentRect.sizeDelta = new Vector2(container.sizeDelta.x, 0);
      scrollRect.content = contentRect;

      // Debug content before layout
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"StatusContent (pre-layout): sizeDelta={contentRect.sizeDelta}, childCount={contentRect.childCount}");
      }

      // Move children to content
      var children = new List<Transform>();
      for (int i = 0; i < container.childCount; i++)
      {
        var child = container.GetChild(i);
        if (child != viewportObj.transform)
          children.Add(child);
      }
      foreach (var child in children)
        child.SetParent(contentRect, false);

      // Add VerticalLayoutGroup
      VerticalLayoutGroup layout = contentRect.GetComponent<VerticalLayoutGroup>();
      if (layout == null)
      {
        layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 0f;
        layout.padding = new RectOffset(0, 0, 0, 0);
        if (KLINEMod.debugLog) MelonLogger.Msg("Added VerticalLayoutGroup to StatusContent.");
      }

      // Debug VerticalLayoutGroup
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"VerticalLayoutGroup: spacing={layout.spacing}, padding={layout.padding.left},{layout.padding.right},{layout.padding.top},{layout.padding.bottom}, " +
                        $"childForceExpandHeight={layout.childForceExpandHeight}");
      }

      // Add ContentSizeFitter
      if (contentRect.GetComponent<ContentSizeFitter>() == null)
      {
        var fitter = contentRect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        if (KLINEMod.debugLog) MelonLogger.Msg("Added ContentSizeFitter to StatusContent.");
      }

      // Add Scrollbar
      GameObject scrollbarObj = new GameObject("Scrollbar");
      scrollbarObj.transform.SetParent(container.transform, false);
      var scrollbarRect = scrollbarObj.AddComponent<RectTransform>();
      var scrollbar = scrollbarObj.AddComponent<Scrollbar>();
      scrollbar.direction = Scrollbar.Direction.BottomToTop;

      // Scrollbar settings
      RectTransform mainScrollbarRect = null;
      float scrollbarWidth = 3f;
      if (mainScrollRect != null)
      {
        if (mainScrollRect.verticalScrollbar != null)
        {
          mainScrollbarRect = mainScrollRect.verticalScrollbar.GetComponent<RectTransform>();
          scrollbarWidth = Mathf.Clamp(mainScrollbarRect.sizeDelta.x, 2f, 10f);
          if (KLINEMod.debugLog) MelonLogger.Msg($"Using MainScrollRect.verticalScrollbar: width={scrollbarWidth}");
        }
        else
        {
          var scrollbarChild = mainScrollRect.GetComponentsInChildren<Scrollbar>()
              .FirstOrDefault(s => s.direction == Scrollbar.Direction.BottomToTop);
          if (scrollbarChild != null)
          {
            mainScrollbarRect = scrollbarChild.GetComponent<RectTransform>();
            scrollbarWidth = Mathf.Clamp(mainScrollbarRect.sizeDelta.x, 2f, 10f);
            if (KLINEMod.debugLog) MelonLogger.Msg($"Found Scrollbar in MainScrollRect children: width={scrollbarWidth}");
          }
        }
        if (mainScrollbarRect == null)
        {
          if (KLINEMod.debugLog) MelonLogger.Msg($"No scrollbar found; using fixed width={scrollbarWidth}");
        }
      }

      // Ensure content width is positive
      if (container.sizeDelta.x <= scrollbarWidth)
      {
        container.sizeDelta = new Vector2(scrollbarWidth + 100, container.sizeDelta.y);
        viewportRect.sizeDelta = container.sizeDelta;
        if (KLINEMod.debugLog) MelonLogger.Warning($"Adjusted container.sizeDelta to {container.sizeDelta} to accommodate scrollbar.");
      }

      // Apply scrollbar settings
      if (mainScrollbarRect != null && mainScrollbarRect.sizeDelta.y >= 0)
      {
        scrollbarRect.anchorMin = mainScrollbarRect.anchorMin;
        scrollbarRect.anchorMax = mainScrollbarRect.anchorMax;
        scrollbarRect.offsetMin = mainScrollbarRect.offsetMin;
        scrollbarRect.offsetMax = mainScrollbarRect.offsetMax;
        scrollbarRect.sizeDelta = new Vector2(scrollbarWidth, 0);
        scrollbarRect.pivot = mainScrollbarRect.pivot;
      }
      else
      {
        scrollbarRect.anchorMin = new Vector2(1, 0);
        scrollbarRect.anchorMax = new Vector2(1, 1);
        scrollbarRect.offsetMin = new Vector2(-scrollbarWidth, 0);
        scrollbarRect.offsetMax = new Vector2(0, 0);
        scrollbarRect.sizeDelta = new Vector2(scrollbarWidth, 0);
        scrollbarRect.pivot = new Vector2(0.5f, 0.5f);
      }

      // Debug scrollbar
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"Scrollbar: sizeDelta={scrollbarRect.sizeDelta}, anchorMin={scrollbarRect.anchorMin}, " +
                        $"anchorMax={scrollbarRect.anchorMax}, offsetMin={scrollbarRect.offsetMin}, offsetMax={scrollbarRect.offsetMax}, pivot={scrollbarRect.pivot}");
      }

      // Adjust viewport and content
      viewportRect.offsetMax = new Vector2(-scrollbarWidth, 0);
      contentRect.sizeDelta = new Vector2(container.sizeDelta.x - scrollbarWidth, 0);

      // Debug viewport and content
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"Viewport (adjusted): sizeDelta={viewportRect.sizeDelta}, offsetMax={viewportRect.offsetMax}");
        MelonLogger.Msg($"StatusContent (adjusted): sizeDelta={contentRect.sizeDelta}");
      }

      // Add scrollbar handle
      GameObject handleObj = new GameObject("Handle");
      handleObj.transform.SetParent(scrollbarObj.transform, false);
      var handleRect = handleObj.AddComponent<RectTransform>();
      handleRect.anchorMin = new Vector2(0, 0);
      handleRect.anchorMax = new Vector2(1, 1);
      handleRect.sizeDelta = new Vector2(scrollbarWidth, 0);
      var handleImage = handleObj.AddComponent<Image>();
      handleImage.color = Color.gray;
      scrollbar.targetGraphic = handleImage;
      scrollbar.handleRect = handleRect;

      // Debug handle
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"Scrollbar Handle: sizeDelta={handleRect.sizeDelta}, anchorMin={handleRect.anchorMin}, anchorMax={handleRect.anchorMax}");
      }

      scrollRect.verticalScrollbar = scrollbar;
      scrollRect.verticalScrollbarVisibility = mainScrollRect != null && mainScrollRect.verticalScrollbarVisibility != 0
          ? mainScrollRect.verticalScrollbarVisibility
          : ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
      scrollRect.verticalScrollbarSpacing = mainScrollRect != null ? mainScrollRect.verticalScrollbarSpacing : -3f;

      // Force initial layout rebuild
      RefreshLayoutGroupsImmediateAndRecursive(contentObj);
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"StatusContent (post-layout): sizeDelta={contentRect.sizeDelta}, childCount={contentRect.childCount}");
      }
    }

    [HarmonyPrefix]
    [HarmonyPatch("RefreshContent")]
    public static bool RefreshContentPrefix(DeliveryApp __instance, bool keepScrollPosition)
    {
      float scrollPos = __instance.MainScrollRect != null ? __instance.MainScrollRect.verticalNormalizedPosition : 0f;
      ScrollRect statusScrollRect = __instance.StatusDisplayContainer.GetComponent<ScrollRect>();
      float statusScrollPos = statusScrollRect != null ? statusScrollRect.verticalNormalizedPosition : 0f;

      if (__instance.MainLayoutGroup != null)
        LayoutRebuilder.ForceRebuildLayoutImmediate(__instance.MainLayoutGroup.GetComponent<RectTransform>());
      var contentRect = statusScrollRect != null && statusScrollRect.content != null
          ? statusScrollRect.content
          : __instance.StatusDisplayContainer;
      if (contentRect != null)
        RefreshLayoutGroupsImmediateAndRecursive(contentRect.gameObject);

      // Debug content after refresh
      if (KLINEMod.debugLog && contentRect != null)
      {
        MelonLogger.Msg($"RefreshContent: StatusContent sizeDelta={contentRect.sizeDelta}, childCount={contentRect.childCount}, " +
                        $"scrollPos={statusScrollPos}");
      }

      if (keepScrollPosition)
      {
        if (__instance.MainScrollRect != null)
          __instance.MainScrollRect.verticalNormalizedPosition = scrollPos;
        if (statusScrollRect != null)
          statusScrollRect.verticalNormalizedPosition = Mathf.Clamp01(statusScrollPos);
      }

      if (KLINEMod.debugLog) MelonLogger.Msg("Refreshed DeliveryApp content, including StatusContent.");
      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("CreateDeliveryStatusDisplay")]
    public static bool CreateDeliveryStatusDisplayPrefix(DeliveryInstance instance, DeliveryApp __instance)
    {
      ScrollRect scrollRect = __instance.StatusDisplayContainer.GetComponent<ScrollRect>();
      RectTransform contentRect = scrollRect != null && scrollRect.content != null
          ? scrollRect.content
          : __instance.StatusDisplayContainer;

      DeliveryStatusDisplay deliveryStatusDisplay = Object.Instantiate(__instance.StatusDisplayPrefab, contentRect);
      deliveryStatusDisplay.AssignDelivery(instance);
      __instance.statusDisplays.Add(deliveryStatusDisplay);
      __instance.SortStatusDisplays();
      __instance.RefreshContent(true);
      __instance.RefreshNoDeliveriesIndicator();

      // Debug DeliveryStatusDisplay
      if (KLINEMod.debugLog)
      {
        MelonLogger.Msg($"Created DeliveryStatusDisplay for {instance.DeliveryID} in StatusContent, sizeDelta={deliveryStatusDisplay.Rect.sizeDelta}, " +
                        $"active={deliveryStatusDisplay.gameObject.activeSelf}");
      }

      return false;
    }
  }
}