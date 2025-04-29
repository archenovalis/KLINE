using MelonLoader;
using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI.Phone.Delivery;
using UnityEngine;
using Il2CppScheduleOne.DevUtilities;
using Unity.Mathematics;

using static KLINE_Standard.KLINEUtilities;
using Il2Cpp;

namespace KLINE_Standard
{
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
      int vehicleCount = CalculateVehicleCount(KLINEUtilities.ConvertList(__instance.listingEntries));
      __result = cartCost + __instance.DeliveryFee * vehicleCount;
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
      List<StringIntPair> orderItems = ConvertList(__instance.listingEntries)
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
      int vehicleCount = CalculateVehicleCount(ConvertList(__instance.listingEntries));
      float deliveryFeeTotal = __instance.DeliveryFee * vehicleCount;
      __instance.DeliveryFeeLabel.text = MoneyManager.FormatAmount(deliveryFeeTotal, false, false);
      return false;
    }
  }
}