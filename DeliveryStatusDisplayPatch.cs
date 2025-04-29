using MelonLoader;
using HarmonyLib;
using ScheduleOne;
using ScheduleOne.Delivery;
using ScheduleOne.ItemFramework;
using ScheduleOne.UI.Phone.Delivery;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using ScheduleOne.DevUtilities;

namespace KLINE
{
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
}