using HarmonyLib;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.UI.Phone.Delivery;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using MelonLoader;
using UnityEngine.EventSystems;

namespace KLINE_Standard
{
  [HarmonyPatch(typeof(DeliveryApp))]
  public class DeliveryAppPatch
  { }
}